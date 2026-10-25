using System.Windows;
using System.Windows.Controls.Primitives;
using AIBoundary.Core;
using AIBoundary.Core.Cameras;
using AIBoundary.Core.Security;
using AIBoundary.Modules;
using AIBoundary.Views;

namespace AIBoundary;

public partial class MainWindow : Window
{
    private readonly TabManager _tabs;

    public MainWindow()
    {
        InitializeComponent();
        _tabs = new TabManager(MainTabs);

        // লগইনের সাথে সাথেই সব ক্যামেরা ব্যাকগ্রাউন্ডে চালু হয় (ট্যাব দেখা না দেখায় কিছু আসে যায় না)
        SafeRunner.Run("CameraHubStart", () =>
        {
            if (Session.Current != null) CameraHub.StartForUser(Session.Current.Id);
        });

        // হোম ট্যাব স্থায়ী; প্রতিটি বাটন আলাদা ট্যাব খোলে
        _tabs.OpenPinned("home", "tab_home", () =>
        {
            var home = new HomeView();
            home.Navigate += OnNavigate;
            return home;
        });

        Loc.Changed += ApplyLanguage;
        Closed += (s, e) => { Loc.Changed -= ApplyLanguage; CameraHub.StopAll(); };
        ApplyLanguage();

        UserText.Text = "👤 " + (Session.Current?.Username ?? "");

        // ইমেইল সেটআপ শুধু অ্যাডমিনের (প্রথম রেজিস্টার করা ইউজার) জন্য
        SafeRunner.Run("AdminCheck", () =>
        {
            var u = Session.Current;
            if (u != null && AuthService.IsAdmin(u.Id)) MiSmtp.Visibility = Visibility.Visible;
        });
    }

    private void ApplyLanguage()
    {
        Title = Loc.Get("app_title");
        MiLanguage.Header = Loc.Get("menu_language");
        MiSmtp.Header = Loc.Get("open_smtp");
        MiLogout.Header = Loc.Get("menu_logout");
    }

    private void OnNavigate(string key)
    {
        switch (key)
        {
            case "ip":
                _tabs.Open("ip", "btn_ip", () =>
                {
                    var m = new IpCameraModule();
                    m.ConnectRequested += OpenIpConnect;
                    return m;
                });
                return;
            case "usb":
                _tabs.Open("usb", "btn_usb", () => new UsbCameraModule());
                return;
            case "single":
                _tabs.Open<SingleCameraModule>("single", "btn_single");
                return;
            case "grid":
                _tabs.Open<GridViewModule>("grid", "btn_grid");
                return;
            case "manage":
                _tabs.Open("manage", () => Tx.Get("cm_tab"), () => new CameraManagementModule());
                return;
            case "telegram":
                _tabs.Open<TelegramModule>("telegram", "btn_telegram");
                return;
            case "worker":
                _tabs.Open<WorkerModule>("worker", "btn_worker");
                return;
            case "camset":
                _tabs.Open("camset", "btn_camset", () =>
                {
                    var m = new CameraSettingsModule();
                    m.BoundaryRequested += OpenBoundary;
                    return m;
                });
                return;
        }

        // বাকি ফিচার পরের ধাপে নিজের ফাইল থেকে আসবে; এখন "পরের ধাপে" দেখায়
        string title = key switch
        {
            "single" => "btn_single",
            "grid" => "btn_grid",
            "camset" => "btn_camset",
            "manage" => "tab_manage",
            "telegram" => "btn_telegram",
            "worker" => "btn_worker",
            "snapshot" => "btn_snapshot",
            _ => ""
        };
        if (title.Length == 0) return;
        _tabs.Open(key, title, () => new ComingSoonModule(title));
    }

    private void OpenBoundary(CameraRecord cam)
    {
        _tabs.Open("boundary:" + cam.Id,
                   () => Tx.Get("tab_boundary") + " – " + cam.Name,
                   () => new BoundaryEditorModule(cam));
    }

    private void OpenIpConnect(string ip)
    {
        _tabs.Open("ipconnect:" + (ip.Length == 0 ? "manual" : ip),
                   () => Tx.Get("tab_ipconnect") + (ip.Length > 0 ? " – " + ip : ""),
                   () => new ConnectIpCameraModule(ip));
    }

    private void BtnMenu_Click(object sender, RoutedEventArgs e)
    {
        MainMenu.PlacementTarget = BtnMenu;
        MainMenu.Placement = PlacementMode.Bottom;
        MainMenu.IsOpen = true;
    }

    private void BtnSmtp_Click(object sender, RoutedEventArgs e) =>
        _tabs.Open<SmtpSetupModule>("smtp", "tab_smtp");

    private void LangBn_Click(object sender, RoutedEventArgs e) => ChangeLanguage("bn");
    private void LangEn_Click(object sender, RoutedEventArgs e) => ChangeLanguage("en");

    private void ChangeLanguage(string lang)
    {
        SafeRunner.Run("ChangeLanguage", () =>
        {
            Loc.Set(lang);
            var u = Session.Current;
            if (u != null)
            {
                AuthService.SetLanguage(u.Id, lang);          // পরের বার লগইনেও এই ভাষা থাকবে
                Session.Current = u with { Language = lang };
            }
        });
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        SafeRunner.Run("Logout", () =>
        {
            var ok = MessageBox.Show(Loc.Get("logout_confirm"), Loc.Get("app_title"),
                                     MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ok != MessageBoxResult.Yes) return;

            CameraHub.StopAll();
            _tabs.CloseAll();
            Session.Current = null;
            var auth = new AuthWindow();
            Application.Current.MainWindow = auth;
            auth.Show();
            Close();
        });
    }
}
