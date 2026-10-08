using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

// Validates the GetIfTable2 P/Invoke layout against System.Net.NetworkInformation
// and compares the cost of both sampling strategies.

internal static class Program
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

    private static (int rc, List<MibIfRow2> rows) ReadTable()
    {
        int rc = GetIfTable2(out IntPtr ptr);
        var rows = new List<MibIfRow2>();
        if (rc != 0 || ptr == IntPtr.Zero) return (rc, rows);

        int numEntries = Marshal.ReadInt32(ptr);
        int stride = Marshal.SizeOf<MibIfRow2>();
        long baseAddr = ptr.ToInt64() + 8; // ULONG NumEntries + padding to 8
        for (int i = 0; i < numEntries; i++)
            rows.Add(Marshal.PtrToStructure<MibIfRow2>(new IntPtr(baseAddr + (long)i * stride)));

        FreeMibTable(ptr);
        return (rc, rows);
    }

    private static void Main()
    {
        Console.WriteLine($"MibIfRow2 size = {Marshal.SizeOf<MibIfRow2>()} bytes");
        var (rc, rows) = ReadTable();
        Console.WriteLine($"GetIfTable2 rc={rc}, rows={rows.Count}");

        var nics = NetworkInterface.GetAllNetworkInterfaces();
        var byGuid = nics.ToDictionary(n => n.Id.Trim('{', '}'), StringComparer.OrdinalIgnoreCase);
        var byName = nics.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"NetworkInterface count = {nics.Length}");

        Console.WriteLine();
        Console.WriteLine("guid | alias | rx-match | tx-match | aliasName");
        int matched = 0;
        foreach (var r in rows)
        {
            string guid = r.InterfaceGuid.ToString("B").Trim('{', '}');
            bool guidMatch = byGuid.TryGetValue(guid, out var nicGuid);
            bool aliasMatch = byName.TryGetValue(r.Alias, out var nicAlias);
            var nic = nicGuid ?? nicAlias;
            string rxVerdict = "n/a", txVerdict = "n/a";
            if (nic is not null)
            {
                matched++;
                try
                {
                    var s = nic.GetIPv4Statistics();
                    rxVerdict = s.BytesReceived == (long)r.InOctets ? "yes" : $"no({s.BytesReceived} vs {r.InOctets})";
                    txVerdict = s.BytesSent == (long)r.OutOctets ? "yes" : $"no({s.BytesSent} vs {r.OutOctets})";
                }
                catch (Exception ex)
                {
                    rxVerdict = txVerdict = "err:" + ex.GetType().Name;
                }
            }

            Console.WriteLine($"{(guidMatch ? "yes" : "no"),-4} | {(aliasMatch ? "yes" : "no"),-5} | {rxVerdict,-24} | {txVerdict,-24} | '{r.Alias}' type={r.Type} oper={r.OperStatus} flags={r.Flags}");
        }

        Console.WriteLine($"matched {matched}/{rows.Count} rows");

        var sw = Stopwatch.StartNew();
        const int Iter = 200;

        sw.Restart();
        for (int i = 0; i < Iter; i++)
        {
            var list = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var n in list)
            {
                try { _ = n.GetIPv4Statistics().BytesReceived; }
                catch { /* ignore */ }
            }
        }

        double netMs = sw.Elapsed.TotalMilliseconds / Iter;

        sw.Restart();
        for (int i = 0; i < Iter; i++) ReadTable();
        double tableMs = sw.Elapsed.TotalMilliseconds / Iter;

        Console.WriteLine();
        Console.WriteLine($"NetworkInterface strategy : {netMs:F3} ms/sample ({netMs * 10:0.00}% of one core @1Hz)");
        Console.WriteLine($"GetIfTable2 strategy      : {tableMs:F3} ms/sample ({tableMs * 10:0.00}% of one core @1Hz)");
    }
}
