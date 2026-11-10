using System.Windows;
using System.Windows.Controls;

// The settings pages.
partial class ControlCenter
{
    FrameworkElement BuildPage(string name)
    {
        if (name == "Prayer") return BuildPrayerPage();
        var title = new StackPanel();
        title.Children.Add(T(name, 22, Ink, FontWeights.SemiBold));
        return PageFrame(title, new StackPanel());
    }
}
