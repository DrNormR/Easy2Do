using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Easy2Do.Models;
using Easy2Do.Views;

namespace Easy2Do.Services;

public class AlarmService : IDisposable
{
    private DispatcherTimer? _timer;
    private Func<ObservableCollection<Note>>? _getNotesFunc;
    private readonly HashSet<string> _activeAlarms = new();

    private const uint SND_ALIAS = 0x00010000;
    private const uint SND_ASYNC = 0x0001;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

    public void Start(Func<ObservableCollection<Note>> getNotesFunc)
    {
        _getNotesFunc = getNotesFunc;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        if (_timer != null)
        {
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_getNotesFunc is null) return;

        var notes = _getNotesFunc();
        var now = DateTime.Now;

        System.Diagnostics.Debug.WriteLine($"[AlarmService] Tick at {now:T} — checking {notes.Count} notes");

        foreach (var note in notes)
        {
            foreach (var item in note.Items)
            {
                if (!item.HasDueDate) continue;

                System.Diagnostics.Debug.WriteLine(
                    $"[AlarmService]   Item '{item.Text}' DueDate={item.DueDate:g} " +
                    $"Completed={item.IsCompleted} Dismissed={item.IsAlarmDismissed} " +
                    $"Snooze={item.SnoozeUntil:g} IsPast={item.DueDate <= now}");

                if (item.IsCompleted || item.IsAlarmDismissed) continue;
                if (item.DueDate!.Value > now) continue;
                if (item.SnoozeUntil.HasValue && item.SnoozeUntil.Value > now) continue;

                var alarmKey = $"{note.Id}:{item.DueDate:o}:{item.GetHashCode()}";
                if (_activeAlarms.Contains(alarmKey))
                {
                    System.Diagnostics.Debug.WriteLine($"[AlarmService]   Already active: {alarmKey}");
                    continue;
                }

                System.Diagnostics.Debug.WriteLine($"[AlarmService]   >>> FIRING ALARM for '{item.Text}'");
                _activeAlarms.Add(alarmKey);
                ShowAlarm(note.Title, item, alarmKey);
            }
        }
    }

    private void ShowAlarm(string noteTitle, TodoItem item, string alarmKey)
    {
        PlayAlarmSound();

        var window = new AlarmWindow(noteTitle, item);
        window.Closed += (_, _) => _activeAlarms.Remove(alarmKey);
        window.Show();
    }

    private static void PlayAlarmSound()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                PlaySound("SystemExclamation", IntPtr.Zero, SND_ALIAS | SND_ASYNC);
            }
        }
        catch
        {
            // Sound is best-effort
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
