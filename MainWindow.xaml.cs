using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.Generic;

namespace FocusHudWpf;

public partial class MainWindow : Window
{
    private DispatcherTimer _timer;
    private DateTime? _currentTaskStartTime; // Changed from _timeRemaining
    private bool _isPlaying = false;
    
    private TogglService? _togglService;
    private ClickUpService? _clickUpService;
    private int? _togglWorkspaceId;
    private string? _clickUpListId;
    private long? _currentTogglEntryId;

    // Cache
    private List<ClickUpTask>? _cachedTasks;
    private DateTime _lastFetchTime;

    private UserSettings _userSettings;

    public MainWindow()
    {
        InitializeComponent();
        
        _userSettings = UserSettings.Load();
        this.Top = _userSettings.Top;
        this.Left = _userSettings.Left;
        this.Topmost = true;
        
        InitializeServices();

        // Initialize Timer
        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += Timer_Tick;
        
        UpdateTimerDisplay();
    }

    private async void InitializeServices()
    {
        try
        {
            // Load Config
            if (File.Exists("appsettings.json"))
            {
                var jsonText = File.ReadAllText("appsettings.json");
                using var doc = JsonDocument.Parse(jsonText);
                if (doc.RootElement.TryGetProperty("Toggl", out var togglElem) && 
                    togglElem.TryGetProperty("ApiToken", out var tokenElem))
                {
                    var token = tokenElem.GetString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        _togglService = new TogglService(token);
                        _togglWorkspaceId = await _togglService.GetDefaultWorkspaceIdAsync();
                        
                        // Load Projects
                        if (_togglWorkspaceId.HasValue)
                        {
                            var projects = await _togglService.GetProjectsAsync(_togglWorkspaceId.Value);
                            // Add "No Project" option
                            projects.Insert(0, new TogglProject { Id = 0, Name = "No Project" });
                            ProjectComboBox.ItemsSource = projects;
                            ProjectComboBox.SelectedIndex = 0;
                        }
                        
                        // Check if running
                        var current = await _togglService.GetCurrentTimeEntryAsync();
                        if (current != null && current.Duration < 0)
                        {
                            // Sync state: running
                            _isPlaying = true;
                            _currentTogglEntryId = current.Id;
                            // Toggl returns start time. Ensure we treat it correctly.
                            // Assuming deserializer handles timezone, convert to UTC to be safe for duration calc
                            _currentTaskStartTime = current.Start.ToUniversalTime();
                            
                            _timer.Start();
                            UpdatePlayButtonVisuals();
                            TaskNameInput.Text = current.Description ?? "No Description";
                        }
                        else
                        {
                            // Sync state: idle -> Fetch from ClickUp
                            await FetchNextTaskFromClickUp(jsonText);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Config Error: {ex.Message}");
        }
    }

    private async Task FetchNextTaskFromClickUp(string jsonText)
    {
         using var doc = JsonDocument.Parse(jsonText);
         if (doc.RootElement.TryGetProperty("ClickUp", out var cuElem) && 
             cuElem.TryGetProperty("ApiToken", out var tokenElem) &&
             cuElem.TryGetProperty("ListId", out var listElem))
         {
             var token = tokenElem.GetString();
             var listId = listElem.GetString();

             if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(listId))
             {
                 _clickUpListId = listId;
                 _clickUpService = new ClickUpService(token);
                 var tasks = await _clickUpService.GetTasksAsync(listId, true);
                 
                 if (tasks.Count > 0)
                 {
                     TaskNameInput.Text = tasks[0].Name;
                 }
                 else
                 {
                     TaskNameInput.Text = "No active tasks";
                 }
             }
         }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshTasks();
    }

    private async Task RefreshTasks()
    {
        if (_clickUpService == null || string.IsNullOrEmpty(_clickUpListId)) return;

        // Start Shimmer Animation
        ShimmerOverlay.Visibility = Visibility.Visible;
        var shimmerAnim = new System.Windows.Media.Animation.DoubleAnimation(
            -300, 
            300, 
            new Duration(TimeSpan.FromSeconds(1.5)))
        {
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
        };
        ShimmerTransform.BeginAnimation(TranslateTransform.XProperty, shimmerAnim);

        this.Cursor = System.Windows.Input.Cursors.Wait;
        // Delay slightly to let the animation show off (and preventing flash on fast nets)
        // await Task.Delay(500); 
        var tasks = await _clickUpService.GetTasksAsync(_clickUpListId, true);
        
        // Stop Shimmer
        ShimmerTransform.BeginAnimation(TranslateTransform.XProperty, null); // Stop
        ShimmerOverlay.Visibility = Visibility.Collapsed;
        this.Cursor = System.Windows.Input.Cursors.Arrow;
        
        if (tasks != null)
        {
            _cachedTasks = tasks;
            _lastFetchTime = DateTime.Now;
            TasksList.ItemsSource = _cachedTasks;
        }
    }
    
    private async void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.R)
        {
            // Force Refresh
            await RefreshTasks();
        }
        else if (e.Key == Key.Escape)
        {
            SlideOut();
        }
    }

    private void TasksList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TasksList.SelectedItem is ClickUpTask task)
        {
             if (!string.IsNullOrEmpty(task.Name))
             {
                 TaskNameInput.Text = task.Name;
                 // Reset selection so we can click it again if needed? or leave it.
                 TasksList.SelectedItem = null; 
             }
             // SlideOut(); // Don't close on selection, user might want to edit name
        }
    }

    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _isReallyExiting = false;

    private HotKeyHelper? _hotKeyHelper;
    private bool _isVisible = false;
    private const double PANEL_WIDTH = 300;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            WindowBlurHelper.EnableBlur(this);
            WindowBlurHelper.HideFromAltTab(this);
            InitializeTrayIcon();
            InitializeWindowSettings();
            RegisterHotKey();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
        }
    }

    private void InitializeWindowSettings()
    {
        // Set initial position (Left side, full height or large height)
        var workArea = System.Windows.SystemParameters.WorkArea;
        this.Height = workArea.Height - 40; // Slightly smaller than screen
        this.Width = PANEL_WIDTH;
        this.Top = workArea.Top + 20;
        this.Left = -PANEL_WIDTH; // Start hidden off-screen
        
        // Ensure UI is ready
        this.Visibility = Visibility.Visible; 
        // Force refresh layout?
    }

    private void RegisterHotKey()
    {
        _hotKeyHelper = new HotKeyHelper(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        _hotKeyHelper.HotKeyPressed += OnHotKeyPressed;
        // Ctrl + Alt + D
        _hotKeyHelper.Register(ModifierKeys.Control | ModifierKeys.Alt, Key.D);
    }

    private void OnHotKeyPressed()
    {
        ToggleSlide();
    }

    private void ToggleSlide()
    {
        if (_isVisible)
        {
            SlideOut();
        }
        else
        {
            SlideIn();
        }
    }

    private void SlideIn()
    {
        _isVisible = true;
        this.Visibility = Visibility.Visible;
        this.Activate();

        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromSeconds(0.25));
        anim.EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        this.BeginAnimation(Window.LeftProperty, anim);
    }

    private void SlideOut()
    {
        _isVisible = false;
        
        var anim = new System.Windows.Media.Animation.DoubleAnimation(-PANEL_WIDTH, TimeSpan.FromSeconds(0.25));
        anim.EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn };
        anim.Completed += (s, e) => 
        {
            // Optional: Hide entirely regarding performance, but keeping it visible just off-screen is smoother
            // this.Visibility = Visibility.Hidden; 
        };
        this.BeginAnimation(Window.LeftProperty, anim);
    }
    
    // Override closing to clean up hotkey
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isReallyExiting)
        {
            e.Cancel = true; 
            SlideOut();
            return;
        }

        _hotKeyHelper?.Dispose();
        _notifyIcon?.Dispose();
        
        // No need to save position anymore
        // _userSettings.Save(); 
        
        // Call base only if really exiting
        // base.OnClosing(e); // Base is called automatically if not cancelled? No.
        // If we cancel, we return. If we don't, we fall through.
    }

    // Reuse timer for date change check?
    private DateTime _lastDateCheck = DateTime.Today;

    private void Timer_Tick(object? sender, EventArgs e)
    {
        UpdateTimerDisplay();
        
        if (DateTime.Today != _lastDateCheck)
        {
            _lastDateCheck = DateTime.Today;
            _ = RefreshTasks(); // Auto refresh on new day
        }
    }

    // Tray Icon Click
    private void ToggleWindowVisibility()
    {
        ToggleSlide();
    }
    
    // Border Mouse Down - removed drag move, added slide out on outside click simulation (if we had input hook)
    // For now, dragging is disabled as position is fixed.
    private void Border_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Maybe allow dragging mainly for debug? 
        // Or implement 'click outside to close'? 
        // For now do nothing or maybe just focus.
    }
    private void InitializeTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon();
        _notifyIcon.Icon = System.Drawing.SystemIcons.Application; // Default icon
        _notifyIcon.Visible = true;
        _notifyIcon.Text = "FocusHUD";
        
        // Single click to toggle
        _notifyIcon.Click += (s, args) =>
        {
            // Only toggle on left click to avoid conflict with context menu
            var me = args as System.Windows.Forms.MouseEventArgs;
            if (me?.Button == System.Windows.Forms.MouseButtons.Left)
            {
                ToggleWindowVisibility();
            }
        };

        // Context Menu
        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => 
        {
            _isReallyExiting = true;
            this.Close();
        };
        contextMenu.Items.Add(exitItem);
        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private async void PlayButton_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isPlaying)
        {
           await StopTimer();
        }
        else
        {
           await StartTimer();
        }
    }

    private async Task StartTimer()
    {
        _isPlaying = true;
        _currentTaskStartTime = DateTime.UtcNow; // Set Start Time
        _timer.Start();
        UpdatePlayButtonVisuals();
        UpdateTimerDisplay(); // Immediate update

        if (_togglService != null && _togglWorkspaceId.HasValue)
        {
            var taskName = TaskNameInput.Text;
            var projectId = ProjectComboBox.SelectedValue as int?;
            if (projectId == 0) projectId = null; // "No Project" check

            try 
            {
                await _togglService.StartTimeEntryAsync(taskName, _togglWorkspaceId.Value, projectId);
                // Ideally catch the ID
                var current = await _togglService.GetCurrentTimeEntryAsync();
                if(current != null) _currentTogglEntryId = current.Id;
            }
            catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }
    }

    private async Task StopTimer()
    {
        _isPlaying = false;
        _timer.Stop();
        _currentTaskStartTime = null; // Reset
        UpdatePlayButtonVisuals();
        UpdateTimerDisplay();

        if (_togglService != null && _togglWorkspaceId.HasValue && _currentTogglEntryId.HasValue)
        {
             try 
            {
                await _togglService.StopTimeEntryAsync(_currentTogglEntryId.Value, _togglWorkspaceId.Value);
            }
            catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }
        _currentTogglEntryId = null;
    }

    private void UpdatePlayButtonVisuals()
    {
        // Toggle Icon
        PlayIcon.Text = _isPlaying ? "⏸" : "▶";
        
        // Toggle Color (Subtle feedback)
        // Icon color logic handled in XAML or here if transparent
        PlayIcon.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_isPlaying ? "#818cf8" : "White"));
    }

    private void UpdateTimerDisplay()
    {
        if (_isPlaying && _currentTaskStartTime.HasValue)
        {
            var elapsed = DateTime.UtcNow - _currentTaskStartTime.Value;
            if (elapsed.TotalHours >= 1)
            {
                TimerText.Text = elapsed.ToString(@"h\:mm\:ss");
            }
            else
            {
                TimerText.Text = elapsed.ToString(@"mm\:ss");
            }
        }
        else
        {
            TimerText.Text = "00:00";
        }
    }
}