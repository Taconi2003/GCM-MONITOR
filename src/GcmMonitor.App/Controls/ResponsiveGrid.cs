using System.Windows;
using System.Windows.Controls.Primitives;

namespace GcmMonitor.App.Controls;

/// <summary>Distribui cartões em colunas iguais usando a largura disponível.</summary>
public sealed class ResponsiveGrid : UniformGrid
{
    public double MinimumTileWidth { get; set; } = 220;
    public int MaximumColumns { get; set; } = 4;

    protected override Size MeasureOverride(Size constraint)
    {
        int count = double.IsFinite(constraint.Width)
            ? Math.Clamp((int)(constraint.Width / MinimumTileWidth), 1, MaximumColumns) : MaximumColumns;
        count = Math.Min(count, Math.Max(1, Children.Count));
        if (Columns != count) Columns = count;
        return base.MeasureOverride(constraint);
    }
}
