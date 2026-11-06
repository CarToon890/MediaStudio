using System.Windows;
using System.Windows.Controls;
namespace MediaStudio.Views;

// Two related editor surfaces stack when the available content area is narrow.
public sealed class ResponsiveColumnsPanel : Panel
{
    private const double Gap = 16;
    private const double Breakpoint = 860;
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? Breakpoint : available.Width;
        var columns = width >= Breakpoint;
        double height = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var childWidth = columns ? (width - Gap) * (i == 0 ? .45 : .55) : width;
            InternalChildren[i].Measure(new Size(Math.Max(0, childWidth), double.PositiveInfinity));
            height = columns ? Math.Max(height, InternalChildren[i].DesiredSize.Height) : height + InternalChildren[i].DesiredSize.Height + (i > 0 ? Gap : 0);
        }
        return new Size(width, height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        var columns = final.Width >= Breakpoint;
        double top = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var width = columns ? (final.Width - Gap) * (i == 0 ? .45 : .55) : final.Width;
            var left = columns && i > 0 ? (final.Width - Gap) * .45 + Gap : 0;
            var height = columns ? final.Height : InternalChildren[i].DesiredSize.Height;
            InternalChildren[i].Arrange(new Rect(left, top, Math.Max(0, width), height));
            if (!columns) top += height + Gap;
        }
        return final;
    }
}
