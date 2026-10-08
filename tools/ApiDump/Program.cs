using System.Reflection;
using ScottPlot;

static void Dump(string title, IEnumerable<string> lines)
{
    Console.WriteLine($"===== {title} =====");
    foreach (var l in lines.Distinct().OrderBy(x => x)) Console.WriteLine(l);
    Console.WriteLine();
}

void DumpIface(Type t)
{
    var all = new List<string>();
    void Walk(Type x)
    {
        all.AddRange(x.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m => m.ToString()!));
        all.AddRange(x.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(p => $"{p.PropertyType.Name} {p.Name}"));
        foreach (var i in x.GetInterfaces()) Walk(i);
    }

    Walk(t);
    Dump(t.Name + " members", all.Where(s => s.Contains("Label") || s.Contains("Tick") || s.Contains("Title") || s.Contains("Scale") || s.Contains("Grid")));
}

DumpIface(typeof(IAxis));
DumpIface(typeof(IXAxis));
DumpIface(typeof(IYAxis));

Dump("Plot.Axes helpers", typeof(AxisManager).GetMethods().Select(m => m.Name).Distinct().OrderBy(x => x));

Dump("DefaultGrid type", new[] { typeof(AxisManager).GetProperty("DefaultGrid")!.PropertyType.FullName! });
Dump("FormsPlot-ish types in ScottPlot assembly", Array.Empty<string>());

Dump("TickLabelStyle type", new[] { typeof(IAxis).GetProperty("TickLabelStyle")?.PropertyType.FullName ?? "n/a" });

Dump("AxisManager title/label helpers",
    typeof(AxisManager).GetProperties().Select(p => $"{p.PropertyType.Name} {p.Name}"));
