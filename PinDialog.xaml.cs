using System.Windows;
using System.Windows.Controls;

namespace ChatZ;

public partial class PinDialog : Window
{
    private readonly AppSettings _settings;

    public PinDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        bool enabled = settings.PinEnabled;
        CurrentPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        DisableButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        IntroText.Text = enabled
            ? "PIN zaštita je uključena. Za bilo kakvu promenu unesi trenutni PIN. Novi PIN ostavi prazan ako ga ne menjaš."
            : "ChatZ će tražiti PIN ili lozinku pri pokretanju i kad ga otvoriš iz traya.";
        NewLabel.Text = enabled
            ? "Novi PIN / lozinka (prazno = bez promene)"
            : "Novi PIN / lozinka (najmanje 4 znaka)";
        SaveButton.Content = enabled ? "Sačuvaj" : "Uključi zaštitu";

        AutoLockBox.SelectedItem = AutoLockBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == settings.AutoLockMinutes.ToString())
            ?? AutoLockBox.Items[2];

        Loaded += (_, _) => (enabled ? CurrentBox : NewBox).Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.PinEnabled && !_settings.VerifyPin(CurrentBox.Password))
        {
            ShowError("Trenutni PIN nije tačan.");
            return;
        }

        string pin = NewBox.Password;
        bool changePin = !_settings.PinEnabled || pin.Length > 0;

        if (changePin)
        {
            if (pin.Length < 4) { ShowError("PIN mora imati najmanje 4 znaka."); return; }
            if (pin != RepeatBox.Password) { ShowError("Ponovljeni PIN se ne poklapa."); return; }
            _settings.SetPin(pin);
        }

        _settings.AutoLockMinutes = int.Parse((string)((ComboBoxItem)AutoLockBox.SelectedItem).Tag);
        _settings.Save();
        DialogResult = true;
    }

    private void Disable_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.VerifyPin(CurrentBox.Password))
        {
            ShowError("Za isključivanje unesi trenutni PIN.");
            return;
        }
        _settings.ClearPin();
        _settings.Save();
        DialogResult = true;
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }
}
