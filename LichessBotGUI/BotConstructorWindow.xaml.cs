using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace LichessBotGUI
{
    public partial class BotConstructorWindow : Window
    {
        private readonly string _botDirectory;
        private List<BotProfile> _profiles = new();
        private BotProfile? _selected;
        private bool _loading;
        private bool _dirty;
        private bool _tokenVisible;

        private Button[] TcButtons => new[] { BtnTCHyper, BtnTCBullet, BtnTCBlitz, BtnTCRapid, BtnTCClassical, BtnTCCustom };

        public BotConstructorWindow(string botDirectory)
        {
            InitializeComponent();
            _botDirectory = botDirectory;

            int cpuCores = Environment.ProcessorCount;
            long totalRamMB = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
            int maxHash = (int)Math.Min(totalRamMB / 2, 16384);
            maxHash = Math.Max(maxHash, 64);

            SliderThreads2.Maximum = cpuCores;
            SliderHash2.Maximum = maxHash;

            LoadProfiles();
        }

        private void LoadProfiles()
        {
            _profiles = ProfileManager.Load(_botDirectory);
            if (_profiles.Count == 0)
            {
                var def = new BotProfile { Name = "Default", IsActive = true };
                _profiles.Add(def);
                ProfileManager.Save(_botDirectory, _profiles);
            }
            RefreshSidebar();
            var active = _profiles.FirstOrDefault(p => p.IsActive) ?? _profiles[0];
            SelectProfile(active);
        }

        private void RefreshSidebar()
        {
            LblProfileCount.Text = $"{_profiles.Count} PROFILE{(_profiles.Count == 1 ? "" : "S")}";
            ProfileList.Items.Clear();
            foreach (var p in _profiles)
            {
                var card = BuildProfileCard(p);
                ProfileList.Items.Add(card);
            }
            BtnDeleteProfile.IsEnabled = _profiles.Count > 1;
        }

        private FrameworkElement BuildProfileCard(BotProfile p)
        {
            bool isSelected = _selected?.Id == p.Id;
            bool isDirty = isSelected && _dirty;

            Color chipColor;
            try { chipColor = (Color)ColorConverter.ConvertFromString(p.ColorTag); }
            catch { chipColor = (Color)ColorConverter.ConvertFromString("#d4985a"); }

            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 0),
                Cursor = Cursors.Hand,
                Tag = p.Id,
            };

            border.Background = isSelected
                ? new SolidColorBrush(Color.FromRgb(0x28, 0x20, 0x18))
                : new SolidColorBrush(Color.FromRgb(0x1e, 0x18, 0x12));

            border.BorderThickness = new Thickness(1);
            border.BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromArgb(0x73, chipColor.R, chipColor.G, chipColor.B))
                : new SolidColorBrush(Color.FromRgb(0x3a, 0x2c, 0x20));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var strip = new System.Windows.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(chipColor),
                RadiusX = 2, RadiusY = 2,
            };
            Grid.SetColumn(strip, 0);

            var textStack = new StackPanel { Margin = new Thickness(8, 0, 4, 0) };
            Grid.SetColumn(textStack, 1);

            string displayName = p.Name + (isDirty ? " *" : "");
            var nameBlock = new TextBlock
            {
                Text = displayName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xf0, 0xeb, 0xe4)),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var subtitleBlock = new TextBlock
            {
                Text = $"{p.TcPreset} {FormatTC(p)} · Skill {p.SkillLevel}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0xa0, 0x92, 0x82)),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            textStack.Children.Add(nameBlock);
            textStack.Children.Add(subtitleBlock);

            grid.Children.Add(strip);
            grid.Children.Add(textStack);

            if (p.IsActive)
            {
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 2, 5, 2),
                    Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x28, 0x10)),
                };
                Grid.SetColumn(badge, 2);
                var badgeText = new TextBlock
                {
                    Text = "● ACTIVE",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x62, 0x99, 0x24)),
                };
                badge.Child = badgeText;
                grid.Children.Add(badge);
            }

            border.Child = grid;
            border.MouseLeftButtonDown += (s, e) =>
            {
                if (s is Border b && b.Tag is Guid id)
                {
                    var profile = _profiles.FirstOrDefault(x => x.Id == id);
                    if (profile != null) SelectProfile(profile);
                }
            };
            return border;
        }

        private static string FormatTC(BotProfile p)
        {
            string mins = p.TcMinutes % 1 == 0
                ? ((int)p.TcMinutes).ToString()
                : p.TcMinutes.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            return $"{mins}+{p.TcIncrement}";
        }

        private void SelectProfile(BotProfile profile)
        {
            _selected = profile;
            _dirty = false;
            _loading = true;
            try
            {
                PopulateEditor(profile);
            }
            finally
            {
                _loading = false;
            }
            RefreshSidebar();
        }

        private void PopulateEditor(BotProfile p)
        {
            TxtProfileName.Text = p.Name;
            SelectColorSwatch(p.ColorTag);

            PwdApiToken.Password = p.ApiToken;
            TxtApiTokenVisible.Text = p.ApiToken;

            TxtEnginePath2.Text = p.EnginePath;
            ChkUseNNUE.IsChecked = p.UseNNUE;
            SliderSkill2.Value = p.SkillLevel;
            SliderSpeed2.Value = p.MoveSpeed;
            SliderDepth2.Value = p.MaxDepth;
            SliderThreads2.Value = p.Threads;
            SliderHash2.Value = p.HashMB;
            TxtMoveOverhead2.Text = p.MoveOverheadMs.ToString();
            ChkPonder2.IsChecked = p.Ponder;

            ChkAutoChallenger.IsChecked = p.AutoChallenger;
            ChkRated2.IsChecked = p.Rated;
            ChkAutoResign2.IsChecked = p.AutoResign;
            TxtResignThreshold2.Text = p.ResignThreshold.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            TxtMinRating2.Text = p.MinRating.ToString();
            TxtMaxGames2.Text = p.MaxGames.ToString();
            SliderConcurrent.Value = p.MaxConcurrent;
            ChkAcceptRapid.IsChecked = p.AcceptRapid;
            ChkIncludeChess960.IsChecked = p.IncludeChess960;
            ChkAutoOpenGame.IsChecked = p.AutoOpenGame;
            ChkAcceptRematch.IsChecked = p.AcceptRematch;
            UpdateResignThresholdState();

            SelectTCPreset(p.TcPreset);
            TxtTCMinutes.Text = p.TcMinutes.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            TxtTCIncrement.Text = p.TcIncrement.ToString();

            ChkBookEnabled.IsChecked = p.BookEnabled;
            TxtBookPath2.Text = p.BookPath;

            ChkSendChat.IsChecked = p.SendChat;
            TxtGreeting2.Text = p.Greeting;
            TxtGG2.Text = p.GGMessage;
            UpdateChatFieldsState();
        }

        private void SelectColorSwatch(string colorTag)
        {
            var swatches = new[] { SwatchAmber, SwatchGreen, SwatchBlue, SwatchPurple, SwatchRed, SwatchGold, SwatchGray, SwatchWhite };
            foreach (var s in swatches)
                s.IsChecked = string.Equals((string)s.Tag, colorTag, StringComparison.OrdinalIgnoreCase);
        }

        private void SelectTCPreset(string preset)
        {
            var map = new Dictionary<string, Button>
            {
                ["Hyper"] = BtnTCHyper,
                ["Bullet"] = BtnTCBullet,
                ["Blitz"] = BtnTCBlitz,
                ["Rapid"] = BtnTCRapid,
                ["Classical"] = BtnTCClassical,
                ["Custom"] = BtnTCCustom,
            };
            foreach (var kv in map)
                kv.Value.Tag = kv.Key == preset ? "selected" : null;
            CustomTCPanel.Visibility = preset == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MarkDirty()
        {
            if (_loading || _selected == null) return;
            _dirty = true;
            RefreshSidebar();
        }

        private void FlushEditorToSelected()
        {
            if (_selected == null) return;

            _selected.Name = TxtProfileName.Text.Trim();
            _selected.ApiToken = _tokenVisible ? TxtApiTokenVisible.Text : PwdApiToken.Password;

            var swatches = new[] { SwatchAmber, SwatchGreen, SwatchBlue, SwatchPurple, SwatchRed, SwatchGold, SwatchGray, SwatchWhite };
            var checkedSwatch = swatches.FirstOrDefault(s => s.IsChecked == true);
            if (checkedSwatch != null) _selected.ColorTag = (string)checkedSwatch.Tag;

            _selected.EnginePath = TxtEnginePath2.Text.Trim();
            _selected.UseNNUE = ChkUseNNUE.IsChecked == true;
            _selected.SkillLevel = (int)SliderSkill2.Value;
            _selected.MoveSpeed = Math.Round(SliderSpeed2.Value, 1);
            _selected.MaxDepth = (int)SliderDepth2.Value;
            _selected.Threads = (int)SliderThreads2.Value;
            _selected.HashMB = (int)SliderHash2.Value;
            _selected.MoveOverheadMs = int.TryParse(TxtMoveOverhead2.Text, out int mo) ? mo : 100;
            _selected.Ponder = ChkPonder2.IsChecked == true;

            _selected.AutoChallenger = ChkAutoChallenger.IsChecked == true;
            _selected.Rated = ChkRated2.IsChecked == true;
            _selected.AutoResign = ChkAutoResign2.IsChecked == true;
            _selected.ResignThreshold = double.TryParse(TxtResignThreshold2.Text,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double rt) ? rt : -5.0;
            _selected.MinRating = int.TryParse(TxtMinRating2.Text, out int mr) ? mr : 1900;
            _selected.MaxGames = int.TryParse(TxtMaxGames2.Text, out int mg) ? mg : 0;
            _selected.MaxConcurrent = (int)SliderConcurrent.Value;
            _selected.AcceptRapid = ChkAcceptRapid.IsChecked == true;
            _selected.IncludeChess960 = ChkIncludeChess960.IsChecked == true;
            _selected.AutoOpenGame = ChkAutoOpenGame.IsChecked == true;
            _selected.AcceptRematch = ChkAcceptRematch.IsChecked == true;

            var selectedPreset = TcButtons.FirstOrDefault(b => (string?)b.Tag == "selected");
            _selected.TcPreset = selectedPreset?.Name switch
            {
                "BtnTCHyper" => "Hyper",
                "BtnTCBullet" => "Bullet",
                "BtnTCBlitz" => "Blitz",
                "BtnTCRapid" => "Rapid",
                "BtnTCClassical" => "Classical",
                _ => "Custom",
            };

            if (_selected.TcPreset == "Custom")
            {
                _selected.TcMinutes = double.TryParse(TxtTCMinutes.Text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double tcm) ? tcm : 3.0;
                _selected.TcIncrement = int.TryParse(TxtTCIncrement.Text, out int tci) ? tci : 0;
            }
            else
            {
                (_selected.TcMinutes, _selected.TcIncrement) = _selected.TcPreset switch
                {
                    "Hyper" => (0.5, 0),
                    "Bullet" => (1.0, 0),
                    "Blitz" => (3.0, 0),
                    "Rapid" => (10.0, 0),
                    "Classical" => (15.0, 10),
                    _ => (_selected.TcMinutes, _selected.TcIncrement),
                };
            }

            _selected.BookEnabled = ChkBookEnabled.IsChecked == true;
            _selected.BookPath = TxtBookPath2.Text.Trim();

            _selected.SendChat = ChkSendChat.IsChecked == true;
            _selected.Greeting = TxtGreeting2.Text;
            _selected.GGMessage = TxtGG2.Text;
        }

        private bool ValidateAll()
        {
            if (_selected == null) return false;

            if (string.IsNullOrWhiteSpace(TxtProfileName.Text) || TxtProfileName.Text.Trim().Length > 40)
            {
                ShowConstructorToast("Profile name is required (max 40 chars)");
                EditorTabs.SelectedIndex = 0;
                TxtProfileName.BorderBrush = new SolidColorBrush(Color.FromRgb(0xc9, 0x37, 0x2c));
                return false;
            }

            if (ChkAutoResign2.IsChecked == true)
            {
                if (!double.TryParse(TxtResignThreshold2.Text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double rt)
                    || rt < -20.0 || rt > -1.0)
                {
                    ShowConstructorToast("Resign threshold must be between -20.0 and -1.0");
                    EditorTabs.SelectedIndex = 2;
                    TxtResignThreshold2.BorderBrush = new SolidColorBrush(Color.FromRgb(0xc9, 0x37, 0x2c));
                    return false;
                }
            }

            if (!int.TryParse(TxtMinRating2.Text, out int mr2) || mr2 < 1000 || mr2 > 3000)
            {
                ShowConstructorToast("Min rating: valid range 1000–3000");
                EditorTabs.SelectedIndex = 2;
                TxtMinRating2.BorderBrush = new SolidColorBrush(Color.FromRgb(0xc9, 0x37, 0x2c));
                return false;
            }

            if (!int.TryParse(TxtMaxGames2.Text, out int mg2) || mg2 < 0 || mg2 > 999)
            {
                ShowConstructorToast("Max games: 0 = unlimited, max 999");
                EditorTabs.SelectedIndex = 2;
                TxtMaxGames2.BorderBrush = new SolidColorBrush(Color.FromRgb(0xc9, 0x37, 0x2c));
                return false;
            }

            if (!int.TryParse(TxtMoveOverhead2.Text, out int mo2) || mo2 < 0 || mo2 > 5000)
            {
                ShowConstructorToast("Move overhead: valid range 0–5000 ms");
                EditorTabs.SelectedIndex = 1;
                TxtMoveOverhead2.BorderBrush = new SolidColorBrush(Color.FromRgb(0xc9, 0x37, 0x2c));
                return false;
            }

            if (TxtGreeting2.Text.Length > 140 || TxtGG2.Text.Length > 140)
            {
                ShowConstructorToast("Chat messages must be 140 characters or less");
                EditorTabs.SelectedIndex = 5;
                return false;
            }

            return true;
        }

        private void ShowConstructorToast(string msg)
        {
            MessageBox.Show(msg, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void UpdateResignThresholdState()
        {
            TxtResignThreshold2.Opacity = ChkAutoResign2.IsChecked == true ? 1.0 : 0.4;
            TxtResignThreshold2.IsEnabled = ChkAutoResign2.IsChecked == true;
        }

        private void UpdateChatFieldsState()
        {
            ChatFieldsPanel.Opacity = ChkSendChat.IsChecked == true ? 1.0 : 0.4;
            ChatFieldsPanel.IsEnabled = ChkSendChat.IsChecked == true;
        }

        private void AnimateSaveButton()
        {
            var scaleX = new DoubleAnimation(1.0, 1.04, TimeSpan.FromMilliseconds(100)) { AutoReverse = true };
            var scaleY = new DoubleAnimation(1.0, 1.04, TimeSpan.FromMilliseconds(100)) { AutoReverse = true };
            BtnSave.RenderTransformOrigin = new Point(0.5, 0.5);
            if (BtnSave.RenderTransform is not ScaleTransform)
                BtnSave.RenderTransform = new ScaleTransform(1, 1);
            var st = (ScaleTransform)BtnSave.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }

        private void SaveAll()
        {
            if (_selected != null)
                FlushEditorToSelected();
            ProfileManager.Save(_botDirectory, _profiles);
            _dirty = false;
        }

        private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void BtnNewProfile_Click(object sender, RoutedEventArgs e)
        {
            int n = _profiles.Count + 1;
            var p = new BotProfile { Name = $"Profile {n}" };
            _profiles.Add(p);
            RefreshSidebar();
            SelectProfile(p);
        }

        private void BtnDuplicateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            FlushEditorToSelected();
            var copy = _selected.Clone();
            copy.Name = _selected.Name + " (copy)";
            _profiles.Add(copy);
            RefreshSidebar();
            SelectProfile(copy);
        }

        private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || _profiles.Count <= 1) return;
            var result = MessageBox.Show(
                $"Delete '{_selected.Name}'? This cannot be undone.",
                "Delete Profile", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            int idx = _profiles.IndexOf(_selected);
            bool wasActive = _selected.IsActive;
            _profiles.Remove(_selected);

            if (wasActive && _profiles.Count > 0)
                _profiles[Math.Max(0, idx - 1)].IsActive = true;

            RefreshSidebar();
            SelectProfile(_profiles[Math.Max(0, Math.Min(idx, _profiles.Count - 1))]);
        }

        private void BtnImportProfile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Profile JSON (*.json)|*.json",
                Title = "Import Profile",
            };
            if (dlg.ShowDialog() != true) return;
            var imported = ProfileManager.Import(dlg.FileName);
            if (imported == null)
            {
                MessageBox.Show("Could not read the profile file. Make sure it is a valid bot profile export.", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _profiles.Add(imported);
            RefreshSidebar();
            SelectProfile(imported);
        }

        private void BtnExportProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            FlushEditorToSelected();
            var dlg = new SaveFileDialog
            {
                Filter = "Profile JSON (*.json)|*.json",
                FileName = $"{_selected.Name}.json",
                Title = "Export Profile",
            };
            if (dlg.ShowDialog() != true) return;
            ProfileManager.Export(_selected, dlg.FileName);
        }

        private void BtnResetGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            var defaults = new BotProfile();
            int tab = EditorTabs.SelectedIndex;
            _loading = true;
            switch (tab)
            {
                case 0:
                    TxtProfileName.Text = _selected.Name;
                    SelectColorSwatch(defaults.ColorTag);
                    PwdApiToken.Password = "";
                    TxtApiTokenVisible.Text = "";
                    break;
                case 1:
                    TxtEnginePath2.Text = defaults.EnginePath;
                    ChkUseNNUE.IsChecked = defaults.UseNNUE;
                    SliderSkill2.Value = defaults.SkillLevel;
                    SliderSpeed2.Value = defaults.MoveSpeed;
                    SliderDepth2.Value = defaults.MaxDepth;
                    SliderThreads2.Value = defaults.Threads;
                    SliderHash2.Value = defaults.HashMB;
                    TxtMoveOverhead2.Text = defaults.MoveOverheadMs.ToString();
                    break;
                case 2:
                    ChkAutoChallenger.IsChecked = defaults.AutoChallenger;
                    ChkRated2.IsChecked = defaults.Rated;
                    ChkAutoResign2.IsChecked = defaults.AutoResign;
                    TxtResignThreshold2.Text = defaults.ResignThreshold.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                    TxtMinRating2.Text = defaults.MinRating.ToString();
                    TxtMaxGames2.Text = defaults.MaxGames.ToString();
                    SliderConcurrent.Value = defaults.MaxConcurrent;
                    ChkAcceptRapid.IsChecked = defaults.AcceptRapid;
                    ChkIncludeChess960.IsChecked = defaults.IncludeChess960;
                    ChkAutoOpenGame.IsChecked = defaults.AutoOpenGame;
                    ChkAcceptRematch.IsChecked = defaults.AcceptRematch;
                    UpdateResignThresholdState();
                    break;
                case 3:
                    SelectTCPreset(defaults.TcPreset);
                    TxtTCMinutes.Text = defaults.TcMinutes.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                    TxtTCIncrement.Text = defaults.TcIncrement.ToString();
                    break;
                case 4:
                    ChkBookEnabled.IsChecked = defaults.BookEnabled;
                    TxtBookPath2.Text = defaults.BookPath;
                    break;
                case 5:
                    ChkSendChat.IsChecked = defaults.SendChat;
                    TxtGreeting2.Text = defaults.Greeting;
                    TxtGG2.Text = defaults.GGMessage;
                    UpdateChatFieldsState();
                    break;
            }
            _loading = false;
            MarkDirty();
        }

        private void BtnRevert_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            var reloaded = ProfileManager.Load(_botDirectory).FirstOrDefault(p => p.Id == _selected.Id);
            if (reloaded != null)
            {
                int idx = _profiles.IndexOf(_selected);
                _profiles[idx] = reloaded;
                SelectProfile(reloaded);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateAll()) return;
            SaveAll();
            AnimateSaveButton();
            RefreshSidebar();
        }

        private void BtnApplyClose_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateAll()) return;
            SaveAll();
            if (_selected != null)
            {
                ProfileManager.SetActive(_botDirectory, _selected.Id);
                WriteTokenForActive(_selected);
            }
            DialogResult = true;
            Close();
        }

        private void WriteTokenForActive(BotProfile p)
        {
            if (string.IsNullOrEmpty(p.ApiToken)) return;
            string envPath = System.IO.Path.Combine(_botDirectory, ".env");
            var lines = System.IO.File.Exists(envPath)
                ? System.IO.File.ReadAllLines(envPath).ToList()
                : new List<string>();
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith("LICHESS_API_TOKEN="))
                {
                    lines[i] = $"LICHESS_API_TOKEN={p.ApiToken}";
                    found = true;
                    break;
                }
            }
            if (!found) lines.Add($"LICHESS_API_TOKEN={p.ApiToken}");
            System.IO.File.WriteAllLines(envPath, lines);
        }

        private void BtnResetIdentity_Click(object sender, MouseButtonEventArgs e) => ResetTab(0);
        private void BtnResetEngine_Click(object sender, MouseButtonEventArgs e) => ResetTab(1);
        private void BtnResetBehavior_Click(object sender, MouseButtonEventArgs e) => ResetTab(2);
        private void BtnResetTC_Click(object sender, MouseButtonEventArgs e) => ResetTab(3);
        private void BtnResetBook_Click(object sender, MouseButtonEventArgs e) => ResetTab(4);
        private void BtnResetChat_Click(object sender, MouseButtonEventArgs e) => ResetTab(5);

        private void ResetTab(int tabIndex)
        {
            EditorTabs.SelectedIndex = tabIndex;
            BtnResetGroup_Click(this, new RoutedEventArgs());
        }

        private void TxtProfileName_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();

        private void ColorSwatch_Checked(object sender, RoutedEventArgs e) => MarkDirty();

        private void PwdApiToken_PasswordChanged(object sender, RoutedEventArgs e) => MarkDirty();

        private void TxtApiTokenVisible_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_tokenVisible) MarkDirty();
        }

        private void BtnToggleToken_Click(object sender, RoutedEventArgs e)
        {
            _tokenVisible = !_tokenVisible;
            if (_tokenVisible)
            {
                TxtApiTokenVisible.Text = PwdApiToken.Password;
                PwdApiToken.Visibility = Visibility.Collapsed;
                TxtApiTokenVisible.Visibility = Visibility.Visible;
                BtnToggleToken.Content = "Hide";
            }
            else
            {
                PwdApiToken.Password = TxtApiTokenVisible.Text;
                TxtApiTokenVisible.Visibility = Visibility.Collapsed;
                PwdApiToken.Visibility = Visibility.Visible;
                BtnToggleToken.Content = "Show";
            }
        }

        private void BtnBrowseEnginePath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe|All Files (*.*)|*.*", Title = "Select Engine" };
            if (dlg.ShowDialog() == true) { TxtEnginePath2.Text = dlg.FileName; MarkDirty(); }
        }

        private void SliderSkill2_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblSkillVal != null) LblSkillVal.Text = ((int)e.NewValue).ToString();
            MarkDirty();
        }

        private void SliderSpeed2_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblSpeedVal != null) LblSpeedVal.Text = $"{e.NewValue.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}x";
            MarkDirty();
        }

        private void SliderDepth2_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblDepthVal != null)
            {
                int v = (int)e.NewValue;
                LblDepthVal.Text = v == 0 ? "∞ (engine decides)" : v.ToString();
            }
            MarkDirty();
        }

        private void SliderThreads2_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblThreadsVal != null)
            {
                int v = (int)e.NewValue;
                LblThreadsVal.Text = v == 0 ? "Auto" : v.ToString();
            }
            MarkDirty();
        }

        private void SliderHash2_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblHashVal != null) LblHashVal.Text = ((int)e.NewValue).ToString();
            MarkDirty();
        }

        private void ChkAutoResign2_Changed(object sender, RoutedEventArgs e)
        {
            UpdateResignThresholdState();
            MarkDirty();
        }

        private void TxtResignThreshold2_LostFocus(object sender, RoutedEventArgs e)
        {
            TxtResignThreshold2.BorderBrush = (Brush)FindResource("BorderBrush");
            MarkDirty();
        }

        private void TxtMinRating2_LostFocus(object sender, RoutedEventArgs e)
        {
            TxtMinRating2.BorderBrush = (Brush)FindResource("BorderBrush");
            MarkDirty();
        }

        private void TxtMaxGames2_LostFocus(object sender, RoutedEventArgs e)
        {
            TxtMaxGames2.BorderBrush = (Brush)FindResource("BorderBrush");
            MarkDirty();
        }

        private void SliderConcurrent_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblConcurrentVal != null) LblConcurrentVal.Text = ((int)e.NewValue).ToString();
            MarkDirty();
        }

        private void BtnTCPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                string preset = (string)btn.Tag;
                SelectTCPreset(preset);
                MarkDirty();
            }
        }

        private void TxtTCMinutes_LostFocus(object sender, RoutedEventArgs e) => MarkDirty();
        private void TxtTCIncrement_LostFocus(object sender, RoutedEventArgs e) => MarkDirty();

        private void BtnBrowseBookPath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Opening Books (*.bin)|*.bin|All Files (*.*)|*.*", Title = "Select Opening Book" };
            if (dlg.ShowDialog() == true) { TxtBookPath2.Text = dlg.FileName; MarkDirty(); }
        }

        private void BookTab_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void BookTab_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var bin = files.FirstOrDefault(f => f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));
            if (bin == null) return;
            TxtBookPath2.Text = bin;
            AnimateBookDrop();
            MarkDirty();
        }

        private void AnimateBookDrop()
        {
            var anim = new DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(400)) { AutoReverse = false };
            TxtBookPath2.BeginAnimation(OpacityProperty, anim);
        }

        private void ChkSendChat_Changed(object sender, RoutedEventArgs e)
        {
            UpdateChatFieldsState();
            MarkDirty();
        }

        private void TxtGreeting2_TextChanged(object sender, TextChangedEventArgs e)
        {
            int len = TxtGreeting2.Text.Length;
            if (LblGreetingCount != null)
            {
                LblGreetingCount.Text = $"{len} / 140";
                LblGreetingCount.Foreground = len > 140
                    ? new SolidColorBrush(Color.FromRgb(0xc9, 0x37, 0x2c))
                    : (Brush)FindResource("TextSecondaryBrush");
            }
            MarkDirty();
        }

        private void TxtGG2_TextChanged(object sender, TextChangedEventArgs e)
        {
            int len = TxtGG2.Text.Length;
            if (LblGGCount != null)
            {
                LblGGCount.Text = $"{len} / 140";
                LblGGCount.Foreground = len > 140
                    ? new SolidColorBrush(Color.FromRgb(0xc9, 0x37, 0x2c))
                    : (Brush)FindResource("TextSecondaryBrush");
            }
            MarkDirty();
        }
    }
}
