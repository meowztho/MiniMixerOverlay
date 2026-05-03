namespace MiniMixerOverlay.UI.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MiniMixerOverlay.Core;
using MiniMixerOverlay.Core.Models;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly MixerController _controller;
    private string _searchText = string.Empty;
    private bool _showOnlyActive = true;
    private string _statusMessage = "Bereit";
    private int _appCount;

    public ObservableCollection<AppEntry> FilteredEntries { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); ApplyFilter(); }
    }

    public bool ShowOnlyActive
    {
        get => _showOnlyActive;
        set { _showOnlyActive = value; OnPropertyChanged(); ApplyFilter(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public int AppCount
    {
        get => _appCount;
        set { _appCount = value; OnPropertyChanged(); }
    }

    public ICommand ToggleActiveCommand { get; }

    public MainViewModel(MixerController controller)
    {
        _controller = controller;
        _controller.OnSessionsChanged += OnSessionsChanged;
        _controller.OnGuardDecision += OnGuardDecision;
        ToggleActiveCommand = new RelayCommand(() => ShowOnlyActive = !ShowOnlyActive);

        ApplyFilter();
    }

    private void OnSessionsChanged()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = $"{_controller.AppEntries.Count} Anwendung(en) erkannt";
            AppCount = _controller.AppEntries.Count;
            ApplyFilter();
        });
    }

    private void OnGuardDecision(string msg)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = msg;
        });
    }

    private void ApplyFilter()
    {
        FilteredEntries.Clear();
        var entries = _controller.AppEntries.AsEnumerable();

        if (ShowOnlyActive)
        {
            entries = entries.Where(e => e.HasActiveAudio);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var s = SearchText.ToLowerInvariant();
            entries = entries.Where(e => e.DisplayName.ToLowerInvariant().Contains(s) || e.ExeName.ToLowerInvariant().Contains(s));
        }

        foreach (var e in entries)
        {
            var volumePercent = e.CombinedVolume <= 1f ? e.CombinedVolume * 100f : e.CombinedVolume;
            FilteredEntries.Add(new AppEntry
            {
                ExePath = e.ExePath,
                ExeName = e.ExeName,
                DisplayName = e.DisplayName,
                IconBytes = e.IconBytes,
                Sessions = e.Sessions,
                CombinedVolume = volumePercent,
                IsMuted = e.IsMuted,
                HasActiveAudio = e.HasActiveAudio,
                IsSystemSound = e.IsSystemSound,
                Rule = e.Rule
            });
        }
    }

    public void SetVolume(string exePath, float vol) => _controller.SetVolume(exePath, vol);
    public void SetMute(string exePath, bool mute) => _controller.SetMute(exePath, mute);
    public void ToggleFavorite(string exePath) => _controller.ToggleFavorite(exePath);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class RelayCommand : ICommand
{
    private readonly Action _exec;
    private readonly Func<bool>? _can;

    public RelayCommand(Action exec, Func<bool>? can = null)
    {
        _exec = exec;
        _can = can;
    }

    public bool CanExecute(object? p) => _can?.Invoke() ?? true;
    public void Execute(object? p) => _exec();

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
