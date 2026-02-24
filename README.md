# EOSTransport
EOSTransport is a transport for [Mirror Networking](https://github.com/MirrorNetworking/Mirror) using Epic Online Services to provide free P2P services.
This is a full revamp of [Katone's EOSTransport](https://github.com/WeLoveJesusChrist/EOSTransport), which they have unfortunately left the Mirror community.

## How does it work?
EOSTransport is a bridge between Mirror and EOS.
It uses the EOS C# SDK (NOT EOS Plugin for Unity!) to easily link both.

The Auth/Connect Interfaces are used to log in and authenticate the player. 
They are not able to play when not being authenticated, which stops quite a bit of hacking.

The Lobby Interface is used for player matchmaking, and the NAT P2P Interface is used to actually connect players and send data between each other.

## Compatibility Information
### Supported Editor Platforms:
- Windows (x64)
- macOS (Intel, ARM64)
- Linux (x64, ARM64)

### Supported Runtime Platforms:
- Windows (x64)
- macOS (Intel, ARM64)
- Linux (x64, ARM64)
- iOS
- Android
- Meta Quest

### Supported Unity Versions:
| Unity Version | Supported |
|---|---|
| 6000.5 Alpha | Yes* |
| 6000.4 Beta | Yes* |
| 6000.3 LTS | Yes |
| 6000.2 | Yes |
| 6000.1 | Yes |
| 6000.0 LTS | Yes |
| 2023.x | Yes* |
| 2022.3 LTS | Yes |
| 2022.2 | Yes* |
| 2022.1 | Partial** |
| 2021.x | Partial** |
| 2020.x and below | Unsupported |

<small><span style="color:grey"><i>*Untested Version.</i></span></small><br>
<small><span style="color:grey"><i>**Unsupported on Android, due to EOS JDK 11 requirement. For 2021, only 2021.3.41f1 and up meets this requirement.</i></span></small>

## FAQ
#### Is EOSTransport free?
- EOSTransport is 100% free to the players and developers. EOS does not charge you (or even ask for your payment info!) for their services.

#### Is Host Migration included? (Where a new host is assigned when the old one leaves)
- Yes, it is included! There is a toggle on EOSManager to turn it on/off.

#### Why can't we use the Sessions Interface instead of the Lobby Interface?
- The reason we can't is that Sessions doesn't have as many built-in features as Lobbies (like built-in kicking), despite the higher player limit.

#### Why is WebGL not supported?
- WebGL isn't supported because the EOS SDK does not come with a binary that supports WebAssembly, the programming backend for WebGL.
