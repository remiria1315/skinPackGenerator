using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using Microsoft.Win32;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Diagnostics; 
namespace SkinPackGenerator
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<SkinItem> _allSkins = new();
        private ObservableCollection<ConfigItem> _allConfigs = new();
        private ICollectionView _skinView;
        private ICollectionView _configView;

        public MainWindow()
        {
            InitializeComponent();

            SkinListBox.ItemsSource = _allSkins;
            ConfigListBox.ItemsSource = _allConfigs;

            _skinView = CollectionViewSource.GetDefaultView(SkinListBox.ItemsSource);
            _configView = CollectionViewSource.GetDefaultView(ConfigListBox.ItemsSource);
            foreach (var kv in DefaultAnimationRecords.Values)
            {
                _allConfigs.Add(new ConfigItem
                {
                    KeyName = kv.Key,
                    SelectedValue = kv.Value
                });
            }

            SearchTextBox.TextChanged += (s, e) => _skinView.Refresh();
            AnimSearchTextBox.TextChanged += (s, e) => _configView.Refresh();

            _skinView.Filter = (obj) =>
            {
                if (string.IsNullOrWhiteSpace(SearchTextBox.Text) || SearchTextBox.Text == "Search...")
                    return true;

                var item = obj as SkinItem;
                return item != null &&
                       item.FileName.Contains(SearchTextBox.Text, StringComparison.OrdinalIgnoreCase);
            };

            _configView.Filter = (obj) =>
            {
                if (string.IsNullOrWhiteSpace(AnimSearchTextBox.Text) || AnimSearchTextBox.Text == "Search...")
                    return true;

                var item = obj as ConfigItem;
                
                return item != null &&
                       item.KeyName.Contains(AnimSearchTextBox.Text, StringComparison.OrdinalIgnoreCase);
            };

            SetupPlaceholder(SearchTextBox, "Search...");
            SetupPlaceholder(AnimSearchTextBox, "Search...");

                        string initial = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            FilePathTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    string path = FilePathTextBox.Text;

                    if (Directory.Exists(path))
                    {
                        LoadFolder(path);
                    }
                    else
                    {
                        MessageBox.Show("そのパスは存在しないよ！");
                    }
                }
            };

            FilePathTextBox.Text = initial;
            LoadFolder(initial);
        }

        private void SetupPlaceholder(TextBox tb, string placeholder)
        {
            tb.GotFocus += (s, e) => { if (tb.Text == placeholder) tb.Text = ""; };
            tb.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(tb.Text)) tb.Text = placeholder; };
        }

        private void LoadFolder(string path)
        {
            if (!Directory.Exists(path)) return;

            _allSkins.Clear();

            var parent = Directory.GetParent(path);
            if (parent != null)
            {
                _allSkins.Add(new SkinItem
                {
                    FileName = "..",
                    FullPath = parent.FullName,
                    IsFolder = true
                });
            }

            foreach (var dir in Directory.GetDirectories(path))
            {
                _allSkins.Add(new SkinItem
                {
                    FileName = "[Folder] " + Path.GetFileName(dir),
                    FullPath = dir,
                    IsFolder = true
                });
            }

            foreach (var file in Directory.GetFiles(path, "*.png"))
            {
                _allSkins.Add(new SkinItem
                {
                    FileName = Path.GetFileName(file),
                    FullPath = file,
                    IsFolder = false
                });
            }
        }

        private void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                FilePathTextBox.Text = dialog.FolderName;
                LoadFolder(dialog.FolderName);
            }
        }

        private void SkinListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SkinListBox.SelectedItem is SkinItem item && item.IsFolder)
            {
                SkinListBox.SelectedItems.Clear();                   FilePathTextBox.Text = item.FullPath;
                LoadFolder(item.FullPath);
            }
        }


        private string GenerateManifestJson(string packName)
        {
            var manifest = new
            {
                format_version = 1,
                header = new
                {
                    name = packName,
                    description = "",
                    version = new[] { 1, 0, 0 },
                    uuid = Guid.NewGuid().ToString()
                },
                modules = new[]
                {
                    new
                    {
                        type = "skin_pack",
                        uuid = Guid.NewGuid().ToString(),
                        version = new[] { 1, 0, 0 }
                    }
                }
            };

            return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        private string GenerateSkinJson(string packName, List<SkinItem> skins, bool isNoArmor, bool isSlim)
        {
            string geometry = isSlim ? "geometry.humanoid.customSlim" : "geometry.humanoid.custom";
            if (isNoArmor) geometry += "NoArmor";

            var anims = _allConfigs.ToDictionary(c => c.KeyName, c => c.SelectedValue);

            var skinsConfig = new
            {
                serialize_name = packName,
                localization_name = packName,
                skins = skins.Select(s => new
                {
                    localization_name = Path.GetFileNameWithoutExtension(s.FileName),
                    geometry = geometry,
                    texture = s.FileName,
                    animations = anims,
                    enable_attachables = !isNoArmor,
                    type = "free"
                }).ToList()
            };

            return JsonSerializer.Serialize(skinsConfig, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }


        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            bool isNoArmor = NoArmorCheckBox.IsChecked ?? false;
            bool isSlim = ModelTypeToggle.IsChecked ?? false;
            string packName = PackNameTextBox.Text;

            var selectedSkins = SkinListBox.SelectedItems
                .Cast<SkinItem>()
                .Where(s => !s.IsFolder)
                .ToList();

            if (!selectedSkins.Any())
            {
                MessageBox.Show("pngを選択してね！");
                return;
            }

            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), "SkinPack_" + Guid.NewGuid());
                Directory.CreateDirectory(tempPath);

                File.WriteAllText(Path.Combine(tempPath, "manifest.json"),
                    GenerateManifestJson(packName));

                File.WriteAllText(Path.Combine(tempPath, "skins.json"),
                    GenerateSkinJson(packName, selectedSkins, isNoArmor, isSlim));

                foreach (var skin in selectedSkins)
                {
                    File.Copy(skin.FullPath,
                        Path.Combine(tempPath, skin.FileName),
                        true);
                }

                string mcpackPath = Path.Combine(
                    Path.GetTempPath(),
                    packName + "_" + Guid.NewGuid() + ".mcpack");

                ZipFile.CreateFromDirectory(tempPath, mcpackPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = mcpackPath,
                    UseShellExecute = true
                });

                Directory.Delete(tempPath, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        public class SkinItem
        {
            public string FileName { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;
            public bool IsFolder { get; set; }
        }

        public class ConfigItem : INotifyPropertyChanged
        {
            public string KeyName { get; set; } = string.Empty;

            private string _selectedValue = string.Empty;
            public string SelectedValue
            {
                get => _selectedValue;
                set
                {
                    _selectedValue = value;
                    OnPropertyChanged(nameof(SelectedValue));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string name)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
