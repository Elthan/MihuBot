﻿using MihuBot.Helpers;
using SharpCollections.Generic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MihuBot.Reminders
{
    internal sealed class ReminderService : IReminderService
    {
        private readonly BinaryHeap<ReminderEntry> _remindersHeap =
            new BinaryHeap<ReminderEntry>(32);

        private readonly SynchronizedLocalJsonStore<List<ReminderEntry>> _reminders =
            new SynchronizedLocalJsonStore<List<ReminderEntry>>("Reminders.json");

        public ReminderService()
        {
            Logger.DebugLog($"Initializing {nameof(ReminderService)}");

            List<ReminderEntry> reminders = _reminders.QueryAsync(i => i).GetAwaiter().GetResult();

            foreach (var reminder in reminders)
                _remindersHeap.Push(reminder);
        }

        public async ValueTask<IEnumerable<ReminderEntry>> GetAllRemindersAsync()
        {
            return await _reminders.QueryAsync(i => i.ToArray());
        }

        public async ValueTask<IEnumerable<ReminderEntry>> GetRemindersForUserAsync(ulong userId)
        {
            return await _reminders.QueryAsync(reminders => reminders
                .Where(r => r.AuthorId == userId)
                .ToArray());
        }

        public async ValueTask<IEnumerable<ReminderEntry>> GetPendingRemindersAsync()
        {
            var now = DateTime.UtcNow;
            List<ReminderEntry> entries = null;

            try
            {
                lock (_remindersHeap)
                {
                    while (!_remindersHeap.IsEmpty && _remindersHeap.Top.Time < now)
                    {
                        Logger.DebugLog($"Popping reminder from the heap {_remindersHeap.Top}");
                        (entries ??= new List<ReminderEntry>()).Add(_remindersHeap.Pop());
                    }
                }

                if (entries != null)
                {
                    List<ReminderEntry> reminders = await _reminders.EnterAsync();
                    try
                    {
                        foreach (var entry in entries)
                            reminders.Remove(entry);
                    }
                    finally
                    {
                        _reminders.Exit();
                    }

                    entries.RemoveAll(r => now - r.Time > TimeSpan.FromSeconds(10));
                }
            }
            catch (Exception ex)
            {
                Logger.DebugLog(ex.ToString());
            }

            return entries ?? (IEnumerable<ReminderEntry>)Array.Empty<ReminderEntry>();
        }

        public async ValueTask ScheduleAsync(ReminderEntry entry)
        {
            Logger.DebugLog($"Setting reminder entry for {entry}");

            List<ReminderEntry> reminders = await _reminders.EnterAsync();
            try
            {
                reminders.Add(entry);
                lock (_remindersHeap)
                {
                    _remindersHeap.Push(entry);
                }
            }
            finally
            {
                _reminders.Exit();
            }
        }
    }
}
