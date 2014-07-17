﻿namespace MottoBeneApps.GoHome.ActivityTracking
{
    #region Namespace Imports

    using System;
    using System.ComponentModel.Composition;
    using System.Threading.Tasks;

    using Caliburn.Micro;

    using Microsoft.Win32;

    using MottoBeneApps.GoHome.ActivityTracking.Properties;
    using MottoBeneApps.GoHome.DataModels;

    #endregion


    [Export(typeof(IUserActivityTracker))]
    internal sealed class UserActivityTracker : IUserActivityTracker
    {
        #region Constants and Fields

        private static readonly object _syncRoot = new object();

        private readonly IActivityRecordsRepository _activityRecordsRepository;
        private readonly ILog _log = LogManager.GetLog(typeof(UserActivityTracker));
        private DateTime _inputSequenceStartTime = DateTime.MinValue;
        private DateTime _lastUserInputTime = DateTime.MinValue;

        #endregion


        #region Constructors and Destructors

        /// <summary>
        /// Initializes a new instance of the <see cref="T:System.Object"/> class.
        /// </summary>
        [ImportingConstructor]
        public UserActivityTracker(IActivityRecordsRepository activityRecordsRepository)
        {
            _activityRecordsRepository = activityRecordsRepository;
        }

        #endregion


        #region Properties

        public bool IsTracking
        {
            get;
            private set;
        }

        #endregion


        #region Public Methods

        public void Start()
        {
            lock (_syncRoot)
            {
                if (IsTracking)
                {
                    return;
                }

                _log.Info("User activity tracking started.");

                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                SystemEvents.SessionSwitch += OnSessionSwitch;
                SystemEvents.SessionEnded += OnSessionEnded;

                UserInputTracker.UserInputDetected += OnUserInput;
                UserInputTracker.Start();

                IsTracking = true;
            }
        }


        public void Stop()
        {
            lock (_syncRoot)
            {
                if (!IsTracking)
                {
                    return;
                }

                _log.Info("User activity tracking stopped.");

                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.SessionEnded -= OnSessionEnded;

                UserInputTracker.Stop();
                UserInputTracker.UserInputDetected -= OnUserInput;

                TimeSpan activeTime = _lastUserInputTime - _inputSequenceStartTime;

                if (!(activeTime.TotalMilliseconds < Settings.Default.ActiveThreshold))
                {
                    _log.Info(
                        "Added activity period on app shutdown: {0} to {1}.",
                        _inputSequenceStartTime,
                        _lastUserInputTime);

                    _activityRecordsRepository.Add(
                        new ActivityRecord(_inputSequenceStartTime, _lastUserInputTime, false));
                }

                _lastUserInputTime = DateTime.MinValue;
                _inputSequenceStartTime = DateTime.MinValue;

                IsTracking = false;
            }
        }

        #endregion


        #region Methods

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            _log.Info("Power mode changed to '{0}'", e.Mode);
        }


        private void OnSessionEnded(object sender, SessionEndedEventArgs e)
        {
            _log.Info("Session ended with reason: '{0}'", e.Reason);

            Stop();
        }


        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            _log.Info("Session switched to: '{0}'", e.Reason);

            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                case SessionSwitchReason.SessionLogoff:
                    Stop();
                    break;

                case SessionSwitchReason.SessionLogon:
                case SessionSwitchReason.SessionUnlock:
                    Start();
                    break;
            }
        }


        private void OnUserActivity()
        {
            lock (_syncRoot)
            {
                DateTime currentTime = DateTime.Now;

                if (_lastUserInputTime == DateTime.MinValue)
                {
                    ActivityRecord lastActivityRecord = _activityRecordsRepository.GetLastRecord();

                    if (lastActivityRecord != null)
                    {
                        if (lastActivityRecord.Idle)
                        {
                            _log.Info(
                                "Updated last idle activity period end time from {0} to {1}.",
                                lastActivityRecord.EndTime,
                                currentTime);
                            lastActivityRecord.EndTime = currentTime;
                            _activityRecordsRepository.Update(lastActivityRecord);
                        }
                        else
                        {
                            _log.Info(
                                "Added idle activity period: {0} to {1}.",
                                lastActivityRecord.EndTime,
                                currentTime);
                            _activityRecordsRepository.Add(
                                new ActivityRecord(lastActivityRecord.EndTime, currentTime, true));
                        }
                    }

                    _inputSequenceStartTime = currentTime;
                    _lastUserInputTime = currentTime;

                    return;
                }

                TimeSpan idleTime = currentTime - _lastUserInputTime;

                if (idleTime.TotalMilliseconds > Settings.Default.IdleThreshold)
                {
                    TimeSpan activeTime = _lastUserInputTime - _inputSequenceStartTime;

                    if (activeTime.TotalMilliseconds > Settings.Default.ActiveThreshold)
                    {
                        _log.Info("Added activity period: {0} to {1}.", _inputSequenceStartTime, _lastUserInputTime);
                        _activityRecordsRepository.Add(
                            new ActivityRecord(_inputSequenceStartTime, _lastUserInputTime, false));

                        _log.Info("Added idle activity period: {0} to {1}.", _lastUserInputTime, currentTime);
                        _activityRecordsRepository.Add(new ActivityRecord(_lastUserInputTime, currentTime, true));
                    }
                    else
                    {
                        _inputSequenceStartTime = DateTime.MinValue;
                        _lastUserInputTime = DateTime.MinValue;

                        OnUserActivity();

                        return;
                    }

                    _inputSequenceStartTime = currentTime;
                }

                _lastUserInputTime = currentTime;
            }
        }


        private void OnUserInput(object sender, EventArgs eventArgs)
        {
            Task.Run(() => OnUserActivity());
        }

        #endregion
    }
}