# EOSTransport
EOSTransport is a transport for [Mirror Networking](https://github.com/MirrorNetworking/Mirror) using Epic Online Services to provide free P2P services.
This is a full revamp of [Katone's EOSTransport](https://github.com/WeLoveJesusChrist/EOSTransport), which they have unfortunately left the Mirror community.

## How does it work?
EOSTransport is a bridge between Mirror and EOS.
It uses the EOS C# SDK (NOT EOS Plugin for Unity!) to easily link both.

The Auth/Connect Interfaces are used to log in and authenticate the player. 
They are not able to play when not being authenticated, which stops quite a bit of hacking.

The Lobby Interface is used for player matchmaking, and the NAT P2P Interface is used to actually connect players and send data between each other.

## FAQ
Q: Is EOSTransport free?
A: EOSTransport is 100% free to the players and developers. EOS does not charge you (or even ask for your payment info!) for their services.

Q: Is Host Migration included? (Where a new host is assigned when the old one leaves)
A: Yes, it is included! There is a toggle on EOSManager to turn it on/off.

Q: Why can't we use the Sessions Interface instead of the Lobby Interface?
A: The reason we can't is that Sessions doesn't have as many built-in features as Lobbies (like built-in kicking), despite the higher player limit.
