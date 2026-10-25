using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIBoundary.Core;
using AIBoundary.Core.Cameras;
using AIBoundary.Core.Security;
using AIBoundary.Core.Workers;

namespace AIBoundary.Modules;

public partial class WorkerModule : UserControl, IDisposable
{
    private const int ListLimit = 100;

    private static readonly Brush TextDark = Solid(0x0D, 0x2F, 0x55);
    private static readonly Brush TextSoft = Solid(0x4A, 0x6A, 0x8A);
    private static readonly Brush Line = Solid(0xBB, 0xD7, 0xF5);
    private static readonly Brush Green = Solid(0x2E, 0x7D, 0x32);
    private static readonly Brush Red = Solid(0xD3, 0x2F, 0x2F);
    private static readonly Brush SlotBg = Solid(0xF4, 0xF9, 0xFF);

    private static Brush Solid(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    private readonly Dictionary<string, byte[]> _photos = new();
    private readonly Dictionary<string, Image> _slotImages = new();
    private List<Worker> _workers = new();
    private long? _editId;
    private string _editName = "";
    private bool _busy;

    private static long Uid => Session.Current?.Id ?? 0;

    public WorkerModule()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            Loc.Changed -= OnLang; Loc.Changed += OnLang;
            WorkerStore.Changed -= ReloadList; WorkerStore.Changed += ReloadList;
            Apply();
            ReloadList();
        };
        Unloaded += (s, e) => Unsubscribe();
    }

    public void Dispose() => Unsubscribe();

    private void Unsubscribe()
    {
        Loc.Changed -= OnLang;
        WorkerStore.Changed -= ReloadList;
    }

    private void OnLang()
    {
        Apply();
        RebuildList();
    }

    // ---------------- টেক্সট ও দেখানো ----------------

    private void Apply()
    {
        TitleT.Text = Tx.Get("w_title");
        SubT.Text = Tx.Get("w_sub");
        LblName.Text = Tx.Get("w_name");
        LblCode.Text = Tx.Get("w_code");
        LblSection.Text = Tx.Get("w_section");
        LblDept.Text = Tx.Get("w_dept");
        LblIdNum.Text = Tx.Get("w_idnum");
        LblPhotos.Text = Tx.Get("w_photos");
        TipT.Text = Tx.Get("w_photo_tip");
        BtnCancelEdit.Content = Tx.Get("w_cancel_edit");
        ListT.Text = string.Format(Tx.Get("w_list_title"), _workers.Count);
        EmptyT.Text = Tx.Get("w_empty");
        UpdateEditUi();
        BuildSlots();
    }

    private void UpdateEditUi()
    {
        bool editing = _editId != null;
        BtnSave.Content = Tx.Get(editing ? "w_update" : "w_save");
        BtnCancelEdit.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        EditT.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        if (editing) EditT.Text = string.Format(Tx.Get("w_edit_mode"), _editName);
    }

    private void Show(string msg, bool error)
    {
        StatusT.Text = msg;
        StatusT.Foreground = error ? Red : Green;
        StatusT.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BtnSave.IsEnabled = !busy;
        Prog.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
    }

    private Style PillStyle => (Style)Application.Current.FindResource("PillButton");

    // ---------------- ৫ দিকের ছবির ঘর ----------------

    private void BuildSlots()
    {
        PhotoPanel.Children.Clear();
        _slotImages.Clear();

        foreach (var angle in WorkerStore.Angles)
        {
            var a = angle;

            var img = new Image { Width = 130, Height = 130, Stretch = Stretch.Uniform };
            if (_photos.TryGetValue(a, out var bytes)) img.Source = ImageUtil.FromBytes(bytes);
            _slotImages[a] = img;

            var frame = new Border
            {
                Width = 140,
                Height = 140,
                Background = SlotBg,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = img
            };

            var label = new TextBlock
            {
                Text = Tx.Get("ang_" + a),
                FontWeight = FontWeights.SemiBold,
                Foreground = TextDark,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 4)
            };

            var bFile = new Button { Content = Tx.Get("w_file"), Style = PillStyle, Margin = new Thickness(0, 0, 4, 0) };
            bFile.Click += (s, e) => PickFile(a);

            var bCam = new Button { Content = Tx.Get("w_cam"), Style = PillStyle, Margin = new Thickness(0, 0, 4, 0) };
            bCam.Click += (s, e) => PickCamera(a, bCam);

            var bClr = new Button { Content = "✕", Style = PillStyle, Foreground = Red };
            bClr.Click += (s, e) => ClearPhoto(a);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            buttons.Children.Add(bFile);
            buttons.Children.Add(bCam);
            buttons.Children.Add(bClr);

            var col = new StackPanel();
            col.Children.Add(frame);
            col.Children.Add(label);
            col.Children.Add(buttons);

            PhotoPanel.Children.Add(new Border
            {
                Background = Brushes.White,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 10, 10),
                Child = col
            });
        }
    }

    private void SetPhoto(string angle, byte[] jpeg)
    {
        _photos[angle] = jpeg;
        if (_slotImages.TryGetValue(angle, out var img)) img.Source = ImageUtil.FromBytes(jpeg);
    }

    private void ClearPhoto(string angle)
    {
        _photos.Remove(angle);
        if (_slotImages.TryGetValue(angle, out var img)) img.Source = null;
    }

    private async void PickFile(string angle)
    {
        await SafeRunner.RunAsync("WorkerPickFile", async () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp",
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;

            var path = dlg.FileName;
            if (new FileInfo(path).Length > 15L * 1024 * 1024) { Show(Tx.Get("w_err_file"), true); return; }

            var jpeg = await Task.Run(() => ImageUtil.LoadNormalized(path, 800));
            if (jpeg == null) { Show(Tx.Get("w_err_file"), true); return; }

            StatusT.Visibility = Visibility.Collapsed;
            SetPhoto(angle, jpeg);
        });
    }

    private void PickCamera(string angle, Button anchor)
    {
        SafeRunner.Run("WorkerPickCamera", () =>
        {
            var cams = CameraHub.GetCameras();
            if (cams.Count == 0) { Show(Tx.Get("w_err_cam_none"), true); return; }

            var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            menu.Items.Add(new MenuItem { Header = Tx.Get("w_pick_cam"), IsEnabled = false });
            menu.Items.Add(new Separator());
            foreach (var cam in cams)
            {
                var c = cam;
                var mi = new MenuItem { Header = c.Name };
                mi.Click += (s, e) => CaptureFrom(c, angle);
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        });
    }

    private void CaptureFrom(CameraRecord cam, string angle)
    {
        SafeRunner.Run("WorkerCapture", () =>
        {
            var worker = CameraHub.GetWorker(cam.Id);
            byte[]? buf = null;
            if (worker == null || worker.State != CameraState.Online ||
                !worker.CopyBgr(1280, ref buf, out int w, out int h) || buf == null)
            {
                Show(Tx.Get("w_err_cam_offline"), true);
                return;
            }
            StatusT.Visibility = Visibility.Collapsed;
            SetPhoto(angle, ImageUtil.EncodeBgr(buf, w, h, 85));
        });
    }

    // ---------------- সেভ / এডিট ----------------

    private void ClearForm()
    {
        NameBox.Text = CodeBox.Text = SectionBox.Text = DeptBox.Text = IdNumBox.Text = "";
        _photos.Clear();
        _editId = null;
        _editName = "";
        BuildSlots();
        UpdateEditUi();
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await SafeRunner.RunAsync("WorkerSave", async () =>
        {
            long uid = Uid;
            if (uid == 0) { Show(Tx.Get("err_login"), true); return; }

            var name = NameBox.Text.Trim();
            var code = CodeBox.Text.Trim();
            if (name.Length < 2 || name.Length > 60) { Show(Tx.Get("w_err_name"), true); return; }
            if (code.Length == 0 || code.Length > 30) { Show(Tx.Get("w_err_code"), true); return; }

            var missing = WorkerStore.Angles.Where(a => !_photos.ContainsKey(a)).Select(a => Tx.Get("ang_" + a)).ToList();
            if (missing.Count > 0)
            {
                Show(string.Format(Tx.Get("w_err_photos"), string.Join(", ", missing)), true);
                return;
            }

            var input = new WorkerInput(code, name, SectionBox.Text.Trim(), DeptBox.Text.Trim(), IdNumBox.Text.Trim());
            var photos = new Dictionary<string, byte[]>(_photos);
            long? editId = _editId;

            SetBusy(true);
            try
            {
                var result = await Task.Run(() =>
                {
                    var thumb = ImageUtil.MakeThumb(photos["front"], 96);
                    return editId == null
                        ? WorkerStore.Add(uid, input, photos, thumb)
                        : WorkerStore.Update(uid, editId.Value, input, photos, thumb);
                });

                switch (result)
                {
                    case WorkerResult.Ok:
                        ClearForm();
                        Show(Tx.Get(editId == null ? "w_saved" : "w_updated"), false);   // তালিকা নিজে নতুন হয়
                        break;
                    case WorkerResult.Duplicate:
                        Show(Tx.Get("w_err_dup"), true);
                        break;
                    default:
                        Show(Tx.Get("cs_err_db"), true);
                        break;
                }
            }
            finally { SetBusy(false); }
        });
    }

    private void BtnCancelEdit_Click(object sender, RoutedEventArgs e) =>
        SafeRunner.Run("WorkerCancelEdit", () => { ClearForm(); StatusT.Visibility = Visibility.Collapsed; });

    private async void StartEdit(Worker w)
    {
        if (_busy) return;
        await SafeRunner.RunAsync("WorkerEdit", async () =>
        {
            long uid = Uid;
            SetBusy(true);
            try
            {
                var photos = await Task.Run(() => WorkerStore.GetPhotos(uid, w.Id));

                _photos.Clear();
                foreach (var kv in photos) _photos[kv.Key] = kv.Value;

                NameBox.Text = w.Name;
                CodeBox.Text = w.Code;
                SectionBox.Text = w.Section;
                DeptBox.Text = w.Department;
                IdNumBox.Text = w.IdNumber;
                _editId = w.Id;
                _editName = w.Name;

                BuildSlots();
                UpdateEditUi();
                StatusT.Visibility = Visibility.Collapsed;
            }
            finally { SetBusy(false); }
        });
    }

    private void DeleteWorker(Worker w)
    {
        SafeRunner.Run("WorkerDelete", () =>
        {
            var ask = MessageBox.Show(string.Format(Tx.Get("w_delete_confirm"), w.Name), Loc.Get("app_title"),
                                      MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ask != MessageBoxResult.Yes) return;

            if (WorkerStore.Delete(Uid, w.Id))
            {
                if (_editId == w.Id) ClearForm();
                Show(Tx.Get("w_deleted"), false);
            }
            else Show(Tx.Get("cm_fail"), true);
        });
    }

    // ---------------- তালিকা ----------------

    private void ReloadList()
    {
        SafeRunner.Run("WorkerReload", () =>
        {
            _workers = WorkerStore.List(Uid);
            RebuildList();
        });
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RebuildList();

    private void RebuildList()
    {
        SafeRunner.Run("WorkerRebuild", () =>
        {
            ListT.Text = string.Format(Tx.Get("w_list_title"), _workers.Count);

            var q = SearchBox.Text.Trim();
            IEnumerable<Worker> src = _workers;
            if (q.Length > 0)
                src = src.Where(w => w.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                     w.Code.Contains(q, StringComparison.OrdinalIgnoreCase));

            var shown = src.Take(ListLimit + 1).ToList();
            bool more = shown.Count > ListLimit;
            if (more) shown.RemoveAt(shown.Count - 1);

            ListHost.Children.Clear();
            foreach (var w in shown) ListHost.Children.Add(BuildRow(w));

            EmptyT.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            MoreT.Text = more ? Tx.Get("w_more") : "";
        });
    }

    private Border BuildRow(Worker w)
    {
        var del = new Button { Content = Tx.Get("w_delete"), Style = PillStyle, Foreground = Red, Margin = new Thickness(8, 0, 0, 0) };
        del.Click += (s, e) => DeleteWorker(w);
        DockPanel.SetDock(del, Dock.Right);

        var edit = new Button { Content = Tx.Get("w_edit"), Style = PillStyle };
        edit.Click += (s, e) => StartEdit(w);
        DockPanel.SetDock(edit, Dock.Right);

        var thumb = new Image { Width = 56, Height = 56, Stretch = Stretch.UniformToFill, Source = ImageUtil.FromBytes(w.Thumb) };
        var thumbBox = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(28),
            ClipToBounds = true,
            Background = SlotBg,
            Margin = new Thickness(0, 0, 14, 0),
            Child = thumb
        };
        DockPanel.SetDock(thumbBox, Dock.Left);

        var parts = new List<string> { "ID: " + w.Code };
        if (w.Section.Length > 0) parts.Add(w.Section);
        if (w.Department.Length > 0) parts.Add(w.Department);
        if (w.IdNumber.Length > 0) parts.Add("#" + w.IdNumber);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = w.Name, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = TextDark });
        text.Children.Add(new TextBlock
        {
            Text = string.Join("   •   ", parts),
            FontSize = 12,
            Foreground = TextSoft,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var row = new DockPanel { LastChildFill = true };
        row.Children.Add(del);
        row.Children.Add(edit);
        row.Children.Add(thumbBox);
        row.Children.Add(text);

        return new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(14),
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = row
        };
    }
}
