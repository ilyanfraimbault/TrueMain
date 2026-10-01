// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "truemain-capture",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "truemain-capture",
            linkerSettings: [
                .linkedFramework("ScreenCaptureKit"),
                .linkedFramework("AVFoundation"),
                .linkedFramework("CoreMedia"),
                .linkedFramework("AppKit"),
            ]
        )
    ]
)
