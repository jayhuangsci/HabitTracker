using System.Windows.Controls;
namespace HabitTracker.Mobile.Views;
public partial class WorkoutView : UserControl
{
    public WorkoutView() => InitializeComponent();
    public void ConfigureInputModes()
    {
        MainPage.SetInputMode(BarInput, "decimal");
        MainPage.SetInputMode(RepsInput, "numeric");
        MainPage.SetInputMode(RpeInput, "decimal");
    }
}
