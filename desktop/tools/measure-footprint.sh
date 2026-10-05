#!/bin/sh
# The desktop app's real footprint on macOS (#1916): memory and CPU of the
# whole app, sampled over a fixed window while one scenario runs, reported as
# median and p95. Method and results: docs/desktop-footprint.md.
#
#   desktop/tools/measure-footprint.sh <scenario> [seconds=300] [interval=5]
#
# "The app" is every process working for it:
# - the shell (`TrueMain` in the bundle, `truemain-desktop` in a dev build)
#   and its descendants, the capture helper `truemain-capture` included;
# - the WebKit processes (WebContent, Networking, GPU), which are launchd's
#   children, not the app's: they are attributed through their *responsible*
#   process — the grouping Activity Monitor shows under the app — read with
#   `responsibility_get_pid_responsible_for_pid`, which needs no root.
#
# Counters, both from `proc_pid_rusage` (no root, no task port, so it reads
# the hardened WebKit services too):
# - memory: `ri_phys_footprint`, the physical footprint — Activity Monitor's
#   "Memory" column, and what `footprint -p` prints — summed over the tree;
# - CPU: user + system time over each interval, as % of one core.
#
# Writes every sample to `footprint-<scenario>-<timestamp>.csv` in the
# current directory and prints one Markdown row for the doc's table, the
# app's version left for the hand to fill.
# Needs python3 (the Xcode Command Line Tools, already needed to build the app).

set -eu

if [ "$#" -lt 1 ]; then
    echo "usage: $0 <scenario> [seconds=300] [interval=5]" >&2
    exit 64
fi

exec python3 - "$@" <<'PY'
import ctypes
import datetime
import os
import platform
import statistics
import subprocess
import sys
import time

scenario = sys.argv[1]
duration = float(sys.argv[2]) if len(sys.argv) > 2 else 300.0
interval = float(sys.argv[3]) if len(sys.argv) > 3 else 5.0
SHELL_NAMES = {"TrueMain", "truemain-desktop"}

libc = ctypes.CDLL("/usr/lib/libSystem.B.dylib")


class RusageInfoV2(ctypes.Structure):
    _fields_ = [("ri_uuid", ctypes.c_uint8 * 16)] + [
        (name, ctypes.c_uint64)
        for name in (
            "ri_user_time", "ri_system_time", "ri_pkg_idle_wkups", "ri_interrupt_wkups",
            "ri_pageins", "ri_wired_size", "ri_resident_size", "ri_phys_footprint",
            "ri_proc_start_abstime", "ri_proc_exit_abstime", "ri_child_user_time",
            "ri_child_system_time", "ri_child_pkg_idle_wkups", "ri_child_interrupt_wkups",
            "ri_child_pageins", "ri_child_elapsed_abstime", "ri_diskio_bytesread",
            "ri_diskio_byteswritten",
        )
    ]


class Timebase(ctypes.Structure):
    _fields_ = [("numer", ctypes.c_uint32), ("denom", ctypes.c_uint32)]


RUSAGE_INFO_V2 = 2
timebase = Timebase()
libc.mach_timebase_info(ctypes.byref(timebase))
# `ri_*_time` are in mach ticks: 1 ns on Intel, 125/3 ns on Apple silicon.
TICK_NS = timebase.numer / timebase.denom
responsible_for = libc.responsibility_get_pid_responsible_for_pid
responsible_for.argtypes = [ctypes.c_int]
responsible_for.restype = ctypes.c_int


def usage(pid):
    info = RusageInfoV2()
    if libc.proc_pid_rusage(pid, RUSAGE_INFO_V2, ctypes.byref(info)) != 0:
        return None
    cpu_ns = (info.ri_user_time + info.ri_system_time) * TICK_NS
    return info.ri_phys_footprint, cpu_ns


def processes():
    out = subprocess.run(["ps", "-axo", "pid=,ppid=,comm="], capture_output=True, text=True, check=True).stdout
    rows = {}
    for line in out.splitlines():
        parts = line.split(None, 2)
        if len(parts) == 3:
            rows[int(parts[0])] = (int(parts[1]), os.path.basename(parts[2]))
    return rows


def tree():
    """The app's processes now: pid -> name."""
    rows = processes()
    shells = [pid for pid, (_, name) in rows.items() if name in SHELL_NAMES]
    if not shells:
        return {}
    roots = set(shells)
    members = {}
    for pid, (_, name) in rows.items():
        cursor, seen = pid, set()
        while cursor in rows and cursor not in seen:
            if cursor in roots:
                members[pid] = name
                break
            seen.add(cursor)
            cursor = rows[cursor][0]
        else:
            if responsible_for(pid) in roots:
                members[pid] = name
    return members


def percentile(values, share):
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, max(0, round(share * (len(ordered) - 1))))]


if not tree():
    sys.exit("measure-footprint: the app is not running")

stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
csv_path = f"footprint-{scenario}-{stamp}.csv"
memories, cpus = [], []
previous, previous_at = {}, time.monotonic()
for pid in tree():
    reading = usage(pid)
    if reading:
        previous[pid] = reading[1]

with open(csv_path, "w") as csv:
    csv.write("elapsed_s,processes,memory_mb,cpu_percent_of_one_core\n")
    started = time.monotonic()
    while time.monotonic() - started < duration:
        time.sleep(interval)
        now = time.monotonic()
        members = tree()
        memory, cpu_ns, current = 0, 0.0, {}
        for pid in members:
            reading = usage(pid)
            if not reading:
                continue
            memory += reading[0]
            current[pid] = reading[1]
            # A process born during the interval counts from its first reading.
            if pid in previous:
                cpu_ns += max(0.0, reading[1] - previous[pid])
        cpu = 100.0 * cpu_ns / ((now - previous_at) * 1e9)
        previous, previous_at = current, now
        memories.append(memory / 1048576)
        cpus.append(cpu)
        csv.write(f"{now - started:.0f},{len(current)},{memories[-1]:.1f},{cpu:.1f}\n")
        csv.flush()

if not memories:
    sys.exit("measure-footprint: no sample taken; the window is shorter than the interval")

version = platform.mac_ver()[0]
machine = subprocess.run(["sysctl", "-n", "machdep.cpu.brand_string"], capture_output=True, text=True).stdout.strip()
print(f"samples: {csv_path}", file=sys.stderr)
print("| Date | App | Scenario | Memory median / p95 (physical footprint) | CPU median / p95 (% of one core) | macOS | Machine |")
print(
    f"| {datetime.date.today()} | <version> | {scenario} "
    f"| {statistics.median(memories):.0f} / {percentile(memories, 0.95):.0f} MB "
    f"| {statistics.median(cpus):.1f} / {percentile(cpus, 0.95):.1f} % "
    f"| {version} | {machine} |"
)
PY
