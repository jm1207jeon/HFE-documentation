using System.Windows;

namespace HfeLayoutSim.App;

/// <summary>
/// A two-choice dialog whose buttons name the actions. Used wherever the app would otherwise show
/// a bare 예/아니오 MessageBox — the same self-descriptiveness the tool demands of the layouts it
/// judges (C8-06 / ISO 9241-110 ②).
/// </summary>
public partial class ChoiceWindow : Window
{
    /// <summary>1 = first (primary) action, 2 = second, 3 = third; 0 = dismissed.</summary>
    public int Choice { get; private set; }

    public bool ChoseFirst => Choice == 1;

    /// <param name="isSecondCancel">
    /// True when the SECOND button is the safe way out; it then becomes the Escape/default action so
    /// Enter can never commit the risky branch.
    /// </param>
    public ChoiceWindow(string title, string message, string firstLabel, string secondLabel,
        bool isSecondCancel = false, string? thirdLabel = null)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        FirstButton.Content = firstLabel;
        SecondButton.Content = secondLabel;

        if (thirdLabel is not null)
        {
            ThirdButton.Content = thirdLabel;
            ThirdButton.Visibility = Visibility.Visible;
            // three-way: the last button is always the "go back" one
            ThirdButton.IsCancel = true;
            ThirdButton.IsDefault = true;
        }
        else
        {
            // Esc must only ever trigger a button that is genuinely a way out. Marking a real action
            // (e.g. "MES 화면 생성") IsCancel would make Esc perform it — the exact surprise the app's
            // own C8 / ISO 9241-110 rules forbid. With no safe button, Esc falls through to the
            // window's cancel command and leaves Choice at 0.
            SecondButton.IsCancel = isSecondCancel;
            SecondButton.IsDefault = isSecondCancel;
            FirstButton.IsDefault = !isSecondCancel;
        }
    }

    private void OnFirst(object sender, RoutedEventArgs e) => Pick(1);

    private void OnSecond(object sender, RoutedEventArgs e) => Pick(2);

    private void OnThird(object sender, RoutedEventArgs e) => Pick(3);

    private void Pick(int choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
