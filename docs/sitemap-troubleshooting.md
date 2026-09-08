# Sitemap — how it is built, and how to diagnose a search engine refusing it

Written while investigating #1538, where Google Search Console rejected a sitemap that was provably
healthy. The point of this page is that the second half of that investigation cost days: almost every
instinct ("the XML must be malformed", "just resubmit and wait") was wrong, and the tests that
settled it are cheap and repeatable.

## How the sitemap is produced

`@nuxtjs/sitemap` serves `/sitemap.xml` from the web app. Static pages come from the file-based
routes; the one data-driven family is declared by `web/server/routes/__sitemap__/urls.ts`, which
enumerates `/champions/{slug}` from the app's own server routes and decorates each entry with a
day-precision `lastmod`. That source route is cached for an hour at the origin, and the rendered
sitemap is cached for ten minutes — so a cold request pays for the fan-out and can take a few
seconds, while warm ones answer in milliseconds.

Player profiles are deliberately absent; see the comment at the top of that file for why.

## Diagnostic order

Work outwards from the file. Each step is cheap, and each one eliminates a whole class of cause.

### 1. Is the response actually correct?

```bash
curl -sS -i -m 30 https://truemain.lol/sitemap.xml | head -20
```

Expect `200`, `Content-Type: text/xml`, and the `X-Sitemap-*` cache headers. A redirect, a 5xx, or an
HTML body is a real bug — go fix it, the rest of this page does not apply.

### 2. Is the XML valid — not merely well-formed?

Well-formed is not the same as schema-valid, and only the second one tells you anything:

```bash
curl -sS https://truemain.lol/sitemap.xml -o sm.xml
curl -sS -o sitemap.xsd https://www.sitemaps.org/schemas/sitemap/0.9/sitemap.xsd
xmllint --noout --schema sitemap.xsd sm.xml
```

`sm.xml validates` closes the question of the file's shape. Also confirm there is no BOM or stray
byte before the declaration, and that the document is complete:

```bash
head -c 64 sm.xml | xxd     # must start directly with 3c3f 786d 6c ("<?xml")
tail -c 64 sm.xml | xxd     # must contain the closing </urlset>
```

A trailing comment after `</urlset>` is legal and is emitted by the generator — not a defect.

### 3. Is it reachable by something other than you?

Your own `curl` proves very little: you are one network, and search engines are not. Two independent
signals are worth more:

- **`robots.txt`** must be indexable and declare the sitemap.
- **Another crawler.** If a second major search engine has fetched the sitemap and returned 200, the
  file is reachable from the public internet. In #1538 this was the single most clarifying fact.

### 4. Does the crawler actually reach the origin?

This is the step that is almost always skipped, and it is the one that mattered. Read the access log
and find out whether the search engine ever requested the file at all:

```bash
# Adjust to the environment's log access — see the operator notes.
grep '"uri":"/sitemap' <access-log> | grep -oE '"client_ip":"[^"]*"|"status":[0-9]+'
```

Beware the log window: a container's default log budget may only hold a day or so, which is easy to
mistake for "the crawler never came". If the window is too short, capture forward instead — a
persistent filter appending matching lines to a file survives rotation and answers the question
definitively.

**If the search engine reports errors while the access log shows no request from it, the failure is
happening before the origin.** That is not a content problem and no amount of editing the XML will
fix it. Take it to the network layer: what sits in front of the origin, and what does it drop? The
operator notes carry the environment-specific checks — including one class of failure that leaves no
trace in any access log.

## Search Console behaviour that wastes days

- **`Couldn't fetch` and `Couldn't read` are different states.** The first means Google never got the
  bytes; the second means it got something it could not parse. The transition between them is
  information — record which one you are seeing.
- **The URL Inspection live test does not re-queue a sitemap.** It fetches and renders a *page*. It
  writes nothing to the Sitemaps report and does not change a sitemap's status. Testing it repeatedly
  and concluding "I just need to wait" is a trap; only a submission re-queues the sitemap.
- **Resubmitting resets the submission date without accelerating anything.** Google's retry cycle on
  a failed sitemap is slow — potentially weeks.
- **A red state right after submission is not necessarily a failure.** A freshly submitted sitemap can
  display an error before it has been processed at all.
- **Submitting a URL the crawler has never seen** (a different path, or the same one with a query
  string) is the cheapest way to separate "this specific entry is stuck" from "the sitemap cannot be
  reached at all" — it gets a brand-new row with no inherited state. Delete such an entry once the
  real one is green.

## What this does not block

A sitemap in error does not stop indexing. It is declared in `robots.txt`, the pages remain crawlable
and internally linked, and a sitemap accelerates discovery rather than authorising it. Check whether
the crawler is fetching pages before treating a red sitemap row as an emergency.
