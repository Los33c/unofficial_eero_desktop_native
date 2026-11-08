// swift-tools-version:5.9
// eero Desktop (Unofficial) for macOS — native SwiftUI + AppKit app.
// Build with ./build.sh (assembles, signs and zips the .app); `swift build` alone compiles the executable.
import PackageDescription

let package = Package(
    name: "EeroDesktop",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "EeroDesktop",
            path: "Sources/EeroDesktop",
            linkerSettings: [
                .linkedFramework("AppKit"), .linkedFramework("SwiftUI"), .linkedFramework("WebKit"),
                .linkedFramework("CoreBluetooth"), .linkedFramework("Security"), .linkedFramework("CryptoKit"),
                .linkedFramework("UserNotifications"), .linkedFramework("Charts"), .linkedFramework("Network")
            ]
        )
    ]
)
