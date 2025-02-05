﻿using SWTORCombatParser.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SWTORCombatParser.Views.Home_Views
{
    /// <summary>
    /// Interaction logic for ParselySettings.xaml
    /// </summary>
    public partial class ParselySettings : Window
    {
        public ParselySettings()
        {
            InitializeComponent();
            if (Settings.HasSetting("username"))
            {
                UserNameBox.Text = Settings.ReadSettingOfType<string>("username").Trim('"');
                PasswordBox.Password = Crypto.DecryptStringAES(Settings.ReadSettingOfType<string>("password").Trim('"'), "parselyInfo");
                GuildNameBox.Text = Settings.ReadSettingOfType<string>("guild").Trim('"');
            }
            SaveButton.Click += OnSave;
            CancelButton.Click += OnCancel;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var encryptedPassword = Crypto.EncryptStringAES(PasswordBox.Password, "parselyInfo");
            var username = UserNameBox.Text;
            var guild = GuildNameBox.Text;
            Settings.WriteSetting("username", username);
            Settings.WriteSetting("password", encryptedPassword);
            Settings.WriteSetting("guild", guild);

            Close();
        }
        public void DragWindow(object sender, MouseButtonEventArgs args)
        {
            DragMove();
        }
    }
}
