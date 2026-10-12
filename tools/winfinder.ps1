param([int]$ProcId)
Add-Type @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class WF {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static List<string> Find(int pid) {
    var list = new List<string>();
    EnumWindows((h, l) => {
      uint wp; GetWindowThreadProcessId(h, out wp);
      if (wp == (uint)pid) {
        var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        list.Add(h.ToInt64() + "|" + (IsWindowVisible(h) ? "1" : "0") + "|" + (r.R-r.L) + "x" + (r.B-r.T) + "|" + sb.ToString());
      }
      return true;
    }, IntPtr.Zero);
    return list;
  }
}
'@ -Language CSharp
[WF]::Find($ProcId) | ForEach-Object { $_ }
