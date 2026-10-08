using System.Runtime.InteropServices;

namespace NetworkMonitor.Core;

/// <summary>One row of the system interface table (from GetIfTable2).</summary>
public readonly struct IfTableRow
{
    public string Id { get; init; }          // "{guid}" - matches NetworkInterface.Id
    public string Name { get; init; }        // friendly name (alias)
    public string Description { get; init; }
    public uint Type { get; init; }          // IF_TYPE_* (6=Ethernet, 71=802.11, 24=loopback, 131=tunnel)
    public uint OperStatus { get; init; }    // 1 = Up, 2 = Down, 6 = LowerLayerDown
    public byte Flags { get; init; }         // bit 1 = software interface (packet filter / callout adapter)
    public ulong Rx { get; init; }           // InOctets
    public ulong Tx { get; init; }           // OutOctets
    public ulong TxLinkSpeed { get; init; }  // bps
    public ulong RxLinkSpeed { get; init; }  // bps

    public const uint IfTypeLoopback = 24;
    public const uint StatusUp = 1;

    public bool Software => (Flags & 0x02) != 0;

    public bool Connected => OperStatus == StatusUp;

    public long LinkSpeed => (long)Math.Max(TxLinkSpeed, RxLinkSpeed);
}

/// <summary>
/// Reads every interface counter with a single native call.
/// ~1 ms per sample regardless of how many adapters exist, versus ~50 ms when
/// enumerating System.Net.NetworkInformation objects (which issues one
/// GetAdaptersAddresses call per adapter).
/// </summary>
public static class IfTable
{
    private const int IfMaxStringSize = 256;
    private const int IfMaxPhysAddressLength = 32;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MibIfRow2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = IfMaxStringSize + 1)]
        public string Alias;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = IfMaxStringSize + 1)]
        public string Description;

        public uint PhysicalAddressLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = IfMaxPhysAddressLength)]
        public byte[] PhysicalAddress;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = IfMaxPhysAddressLength)]
        public byte[] PermanentPhysicalAddress;

        public uint Mtu;
        public uint Type;
        public uint TunnelType;
        public uint MediaType;
        public uint PhysicalMediumType;
        public uint AccessType;
        public uint DirectionType;
        public byte Flags;
        public uint OperStatus;
        public uint AdminStatus;
        public uint MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetIfTable2(out IntPtr table);

    [DllImport("iphlpapi.dll")]
    private static extern void FreeMibTable(IntPtr table);

    private static readonly int RowSize = Marshal.SizeOf<MibIfRow2>();

    /// <summary>Returns a snapshot of every interface. Allocates; call from the sampler thread only.</summary>
    public static List<IfTableRow> Read()
    {
        var result = new List<IfTableRow>(16);
        int rc = GetIfTable2(out IntPtr ptr);
        if (rc != 0 || ptr == IntPtr.Zero) return result;

        try
        {
            int count = Marshal.ReadInt32(ptr);          // ULONG NumEntries
            long baseAddr = ptr.ToInt64() + 8;           // padding so rows stay 8-byte aligned
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibIfRow2>(new IntPtr(baseAddr + (long)i * RowSize));
                result.Add(new IfTableRow
                {
                    // .NET's NetworkInterface.Id uses upper-case GUIDs; keep the same casing so
                    // existing database rows stay aggregated under one key.
                    Id = row.InterfaceGuid.ToString("B").ToUpperInvariant(),
                    Name = row.Alias ?? string.Empty,
                    Description = row.Description ?? string.Empty,
                    Type = row.Type,
                    OperStatus = row.OperStatus,
                    Flags = row.Flags,
                    Rx = row.InOctets,
                    Tx = row.OutOctets,
                    TxLinkSpeed = row.TransmitLinkSpeed,
                    RxLinkSpeed = row.ReceiveLinkSpeed,
                });
            }
        }
        finally
        {
            FreeMibTable(ptr);
        }

        return result;
    }
}
