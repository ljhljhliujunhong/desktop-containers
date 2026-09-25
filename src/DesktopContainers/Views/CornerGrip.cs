using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopContainers;

public sealed class CornerGrip : Border
{
    public CornerGrip()
    {
        Width = 36;
        Height = 36;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Cursor = Cursors.SizeNWSE;
        IsHitTestVisible = true;
    }
}
