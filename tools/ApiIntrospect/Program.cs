using ScottPlot;

var p = new Plot();
Console.WriteLine("default benchmark: " + (p.Benchmark?.ToString() ?? "null"));

p.Add.Scatter(new double[] { 1, 2, 3 }, new double[] { 1, 4, 2 });
if (p.Benchmark is { } bm) bm.IsVisible = false;
var img = p.GetImage(400, 300);
Console.WriteLine("hidden benchmark render OK: " + (img is not null));

p.Axes.AutoScale();
Console.WriteLine("limits after autoscale: " + p.Axes.GetLimits());
p.Axes.SetLimitsY(0, 10);
Console.WriteLine("limits after SetLimitsY: " + p.Axes.GetLimits());
var img2 = p.GetImage(400, 300);
Console.WriteLine("limits after render: " + p.Axes.GetLimits());
Console.WriteLine("benchmark still hidden: " + (p.Benchmark?.IsVisible ?? false));
