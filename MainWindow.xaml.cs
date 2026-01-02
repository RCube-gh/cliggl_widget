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

    public MainWindow()
    {
        InitializeComponent();
        
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
                            TaskNameText.Text = current.Description ?? "No Description";
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
            MessageBox.Show($"Config Error: {ex.Message}");
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
                     TaskNameText.Text = tasks[0].Name;
                 }
                 else
                 {
                     TaskNameText.Text = "No active tasks";
                 }
             }
         }
    }

    private async void TaskName_Click(object sender, MouseButtonEventArgs e)
    {
        if (_clickUpService == null || string.IsNullOrEmpty(_clickUpListId)) return;

        // Toggle Expand/Collapse
        if (this.Height > 100)
        {
            // Collapse
            CollapseWindow();
        }
        else
        {
            // Expand
            // Set height to show list with animation
            TaskListArea.Visibility = Visibility.Visible;

            var anim = new System.Windows.Media.Animation.DoubleAnimation(350, TimeSpan.FromSeconds(0.3));
            anim.EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            this.BeginAnimation(Window.HeightProperty, anim);
            
            // Check Cache
            if (_cachedTasks == null || _cachedTasks.Count == 0 || DateTime.Now > _lastFetchTime.AddMinutes(15))
            {
                 await RefreshTasks();
            }
            else
            {
                // Use Cache
                TasksList.ItemsSource = _cachedTasks;
            }
        }
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

        this.Cursor = Cursors.Wait;
        // Delay slightly to let the animation show off (and preventing flash on fast nets)
        // await Task.Delay(500); 
        var tasks = await _clickUpService.GetTasksAsync(_clickUpListId, true);
        
        // Stop Shimmer
        ShimmerTransform.BeginAnimation(TranslateTransform.XProperty, null); // Stop
        ShimmerOverlay.Visibility = Visibility.Collapsed;
        this.Cursor = Cursors.Arrow;
        
        if (tasks != null)
        {
            _cachedTasks = tasks;
            _lastFetchTime = DateTime.Now;
            TasksList.ItemsSource = _cachedTasks;
        }
    }
    
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.R)
        {
            // Force Refresh
            await RefreshTasks();
        }
    }

    private void TasksList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TasksList.SelectedItem is ClickUpTask task)
        {
             if (!string.IsNullOrEmpty(task.Name))
             {
                 TaskNameText.Text = task.Name;
                 // Reset selection so we can click it again if needed? or leave it.
                 TasksList.SelectedItem = null; 
             }
             CollapseWindow();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            WindowBlurHelper.EnableBlur(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to enable blur: {ex.Message}");
        }
    }

    private void CollapseWindow()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation(60, TimeSpan.FromSeconds(0.3));
        anim.EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        anim.Completed += (s, _) => TaskListArea.Visibility = Visibility.Collapsed;
        this.BeginAnimation(Window.HeightProperty, anim);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        UpdateTimerDisplay();
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
            var taskName = TaskNameText.Text;
            try 
            {
                await _togglService.StartTimeEntryAsync(taskName, _togglWorkspaceId.Value);
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
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isPlaying ? "#818cf8" : "#3e455e"));
        PlayButton.Background = brush;
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

    // Allow dragging the window by clicking anywhere on the border
    private void Border_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            this.DragMove();
        }
    }
}