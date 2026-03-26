using System.Windows.Media;

namespace TombLib.Utils;

public static class BrushHelpers
{
	public static Brush CreateFrozenBrush(Color color)
	{
		var b = new SolidColorBrush(color);
		b.Freeze();
		return b;
	}
}
