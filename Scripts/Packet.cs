using Epic.OnlineServices;
using System;

namespace EpicTransport
{
    /// <summary>
    /// Represents a packet sent to another peer.
    /// </summary>
    public struct Packet
    {
        //4 + 2 + 1 = 7 bytes for the header
        public const int HeaderSize = sizeof(uint) + sizeof(ushort) + sizeof(bool);
        public int Size => HeaderSize + data.Length;

        #region Header
        public uint packetId { get; internal set; }
        public ushort fragmentId { get; internal set; }
        public bool lastFragment { get; internal set; }
        #endregion


        public byte[] data { get; internal set; }

        //old FromBytes()
        public Packet(ArraySegment<byte> array)
        {
            packetId = BitConverter.ToUInt32(array.AsSpan(0, 4));
            fragmentId = BitConverter.ToUInt16(array.AsSpan(4, 2));
            lastFragment = array.Array![array.Offset + 6] == 1;

            data = new byte[array.Count - HeaderSize];
            Array.Copy(array.Array, array.Offset + HeaderSize, data, 0, array.Count - HeaderSize);
        }

        public byte[] ToBytes()
        {
            byte[] array = new byte[Size];

            //write the packet id
            array[0] = (byte)packetId;
            array[1] = (byte)(packetId >> 8);
            array[2] = (byte)(packetId >> 16);
            array[3] = (byte)(packetId >> 24);

            //write the fragment id
            array[4] = (byte)fragmentId;
            array[5] = (byte)(fragmentId >> 8);

            //write the last fragment boolean, used to tell the peer if the data stream is over
            array[6] = lastFragment ? (byte)1 : (byte)0;

            //finalize
            Array.Copy(data, 0, array, HeaderSize, data.Length);

            return array;
        }

        public static int CompareByFragmentID(Packet a, Packet b) => a.fragmentId.CompareTo(b.fragmentId);
    }

    public struct PacketKey
    {
        public ProductUserId peer;
        public uint packetId;
        public byte channel;
    }
}
