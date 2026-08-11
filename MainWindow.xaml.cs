using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace MusicOverlay
{
    public partial class MainWindow : Window
    {
        private GlobalSystemMediaTransportControlsSessionManager? sessionManager;
        private GlobalSystemMediaTransportControlsSession? currentSession;

        private readonly DispatcherTimer updateTimer;
        private readonly DispatcherTimer hideOverlayTimer;

        private bool isDraggingSlider = false;
        private bool isMouseOverProgress = false;

        private TimeSpan currentPosition = TimeSpan.Zero;
        private TimeSpan currentDuration = TimeSpan.Zero;

        private DateTime positionStartedAt = DateTime.UtcNow;

        private bool isPlaying = false;

        private TimeSpan lastApiPosition = TimeSpan.MinValue;
        private bool hasApiPosition = false;

        private string lastSongTitle = "";
        private string lastArtist = "";

        private bool manualTrackChange = false;


        public MainWindow()
        {
            InitializeComponent();

            updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };

            updateTimer.Tick += UpdateTimer_Tick;


            hideOverlayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };

            hideOverlayTimer.Tick += HideOverlayTimer_Tick;

            Loaded += MainWindow_Loaded;
        }


        // =========================================================
        // ПОКАЗ ОВЕРЛЕЯ
        // =========================================================

        private void ShowOverlay()
        {
            if (!IsVisible)
            {
                Show();
            }

            Topmost = true;

            hideOverlayTimer.Stop();
            hideOverlayTimer.Start();
        }


        // =========================================================
        // СКРЫТИЕ
        // =========================================================

        private void HideOverlayTimer_Tick(
            object? sender,
            EventArgs e)
        {
            hideOverlayTimer.Stop();

            if (isMouseOverProgress ||
                isDraggingSlider)
            {
                hideOverlayTimer.Start();
                return;
            }

            Hide();
        }


        // =========================================================
        // ЗАПУСК
        // =========================================================

        private async void MainWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            PositionWindowInCorner();

            try
            {
                sessionManager =
                    await GlobalSystemMediaTransportControlsSessionManager
                        .RequestAsync();

                currentSession =
                    sessionManager.GetCurrentSession();

                if (currentSession != null)
                {
                    await UpdateSongInfo();

                    SyncPosition();

                    UpdatePlaybackState();
                }

                updateTimer.Start();

                ShowOverlay();
            }
            catch
            {
                updateTimer.Start();
            }
        }


        // =========================================================
        // ЛЕВЫЙ ВЕРХНИЙ УГОЛ
        // =========================================================

        private void PositionWindowInCorner()
        {
            try
            {
                Rect workArea =
                    SystemParameters.WorkArea;

                Left =
                    workArea.Left + 20;

                Top =
                    workArea.Top + 20;
            }
            catch
            {
            }
        }


        // =========================================================
        // ГЛАВНЫЙ ТАЙМЕР
        // =========================================================

        private async void UpdateTimer_Tick(
            object? sender,
            EventArgs e)
        {
            try
            {
                if (sessionManager == null)
                    return;


                var session =
                    sessionManager.GetCurrentSession();


                if (session == null)
                    return;


                // =================================================
                // ИЗМЕНИЛАСЬ САМА MEDIA SESSION
                // =================================================

                if (currentSession != session)
                {
                    bool previousTrackWasNearEnd =
                        WasCurrentTrackNearEnd();


                    currentSession = session;

                    hasApiPosition = false;

                    lastApiPosition =
                        TimeSpan.MinValue;


                    lastSongTitle = "";
                    lastArtist = "";


                    await UpdateSongInfo();

                    SyncPosition();

                    UpdatePlaybackState();


                    // Если предыдущий трек НЕ был практически
                    // в самом конце — значит пользователь
                    // переключил его вручную.
                    if (!previousTrackWasNearEnd)
                    {
                        ShowOverlay();
                    }


                    return;
                }


                UpdatePlaybackState();

                CheckExternalPositionChange();

                UpdateDisplayedPosition();

                await CheckSongChange();
            }
            catch
            {
            }
        }


        // =========================================================
        // ПРОВЕРКА — ЗАКАНЧИВАЛСЯ ЛИ ТРЕК
        // =========================================================

        private bool WasCurrentTrackNearEnd()
        {
            try
            {
                if (currentDuration.TotalSeconds <= 0)
                    return false;


                TimeSpan position =
                    currentPosition;


                if (isPlaying)
                {
                    position +=
                        DateTime.UtcNow -
                        positionStartedAt;
                }


                double remaining =
                    (
                        currentDuration -
                        position
                    ).TotalSeconds;


                // Небольшой запас, потому что API может
                // обновляться с задержкой.
                return remaining <= 2.5;
            }
            catch
            {
                return false;
            }
        }


        // =========================================================
        // ПРОВЕРКА СМЕНЫ ТРЕКА
        // =========================================================

        private async System.Threading.Tasks.Task CheckSongChange()
        {
            if (currentSession == null)
                return;


            try
            {
                var properties =
                    await currentSession
                        .TryGetMediaPropertiesAsync();


                if (properties == null)
                    return;


                string title =
                    properties.Title ?? "";


                string artist =
                    properties.Artist ?? "";


                // Первый запуск
                if (lastSongTitle == "")
                {
                    lastSongTitle = title;
                    lastArtist = artist;

                    return;
                }


                // =================================================
                // ТРЕК ИЗМЕНИЛСЯ
                // =================================================

                if (title != lastSongTitle ||
                    artist != lastArtist)
                {
                    bool previousTrackWasNearEnd =
                        WasCurrentTrackNearEnd();


                    lastSongTitle = title;
                    lastArtist = artist;


                    hasApiPosition = false;

                    lastApiPosition =
                        TimeSpan.MinValue;


                    await UpdateSongInfo();

                    SyncPosition();

                    UpdatePlaybackState();


                    // Если трек не был в конце —
                    // пользователь его переключил.
                    if (!previousTrackWasNearEnd)
                    {
                        ShowOverlay();
                    }


                    manualTrackChange = false;
                }
            }
            catch
            {
            }
        }


        // =========================================================
        // PLAY / PAUSE
        // =========================================================

        private void UpdatePlaybackState()
        {
            if (currentSession == null)
                return;


            try
            {
                var playbackInfo =
                    currentSession.GetPlaybackInfo();


                if (playbackInfo == null)
                    return;


                bool newPlayingState =
                    playbackInfo.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;


                if (newPlayingState != isPlaying)
                {
                    bool nearEnd =
                        WasCurrentTrackNearEnd();


                    isPlaying =
                        newPlayingState;


                    positionStartedAt =
                        DateTime.UtcNow;


                    if (!nearEnd)
                    {
                        ShowOverlay();
                    }
                }


                UpdatePlayPauseIcon();
            }
            catch
            {
            }
        }


        // =========================================================
        // ПРОВЕРКА ВНЕШНЕЙ ПЕРЕМОТКИ
        // =========================================================

        private void CheckExternalPositionChange()
        {
            if (currentSession == null)
                return;


            try
            {
                var timeline =
                    currentSession.GetTimelineProperties();


                if (timeline == null)
                    return;


                TimeSpan apiPosition =
                    timeline.Position;


                TimeSpan apiDuration =
                    timeline.EndTime -
                    timeline.StartTime;


                if (apiDuration.TotalSeconds <= 0)
                    return;


                currentDuration =
                    apiDuration;


                if (!hasApiPosition)
                {
                    hasApiPosition = true;

                    lastApiPosition =
                        apiPosition;

                    currentPosition =
                        apiPosition;

                    positionStartedAt =
                        DateTime.UtcNow;


                    UpdateVisualPosition(
                        currentPosition);

                    return;
                }


                double apiDifference =
                    Math.Abs(
                        (
                            apiPosition -
                            lastApiPosition
                        ).TotalSeconds);


                lastApiPosition =
                    apiPosition;


                if (apiDifference >= 1.0)
                {
                    TimeSpan expectedPosition =
                        currentPosition;


                    if (isPlaying)
                    {
                        expectedPosition +=
                            DateTime.UtcNow -
                            positionStartedAt;
                    }


                    double difference =
                        Math.Abs(
                            (
                                apiPosition -
                                expectedPosition
                            ).TotalSeconds);


                    if (difference >= 1.5)
                    {
                        currentPosition =
                            apiPosition;


                        positionStartedAt =
                            DateTime.UtcNow;


                        if (!isDraggingSlider)
                        {
                            UpdateVisualPosition(
                                currentPosition);
                        }


                        // Пользователь перемотал трек
                        ShowOverlay();
                    }
                }
            }
            catch
            {
            }
        }


        // =========================================================
        // ИНФОРМАЦИЯ О ТРЕКЕ
        // =========================================================

        private async System.Threading.Tasks.Task UpdateSongInfo()
        {
            if (currentSession == null)
                return;


            try
            {
                var properties =
                    await currentSession
                        .TryGetMediaPropertiesAsync();


                if (properties == null)
                    return;


                SongTitle.Text =
                    string.IsNullOrWhiteSpace(
                        properties.Title)
                        ? "Название песни"
                        : properties.Title;


                ArtistName.Text =
                    string.IsNullOrWhiteSpace(
                        properties.Artist)
                        ? "Исполнитель"
                        : properties.Artist;


                lastSongTitle =
                    properties.Title ?? "";


                lastArtist =
                    properties.Artist ?? "";


                // =================================================
                // ОБЛОЖКА
                // =================================================

                try
                {
                    if (properties.Thumbnail != null)
                    {
                        using var stream =
                            await properties.Thumbnail
                                .OpenReadAsync();


                        using Stream netStream =
                            stream.AsStream();


                        var bitmap =
                            new BitmapImage();


                        bitmap.BeginInit();


                        bitmap.CacheOption =
                            BitmapCacheOption.OnLoad;


                        bitmap.StreamSource =
                            netStream;


                        bitmap.EndInit();


                        bitmap.Freeze();


                        AlbumArt.Source =
                            bitmap;
                    }
                    else
                    {
                        AlbumArt.Source = null;
                    }
                }
                catch
                {
                    AlbumArt.Source = null;
                }
            }
            catch
            {
            }
        }


        // =========================================================
        // СИНХРОНИЗАЦИЯ
        // =========================================================

        private void SyncPosition()
        {
            if (currentSession == null)
                return;


            try
            {
                var timeline =
                    currentSession.GetTimelineProperties();


                if (timeline == null)
                    return;


                TimeSpan duration =
                    timeline.EndTime -
                    timeline.StartTime;


                if (duration.TotalSeconds <= 0)
                    return;


                currentDuration =
                    duration;


                TimeSpan position =
                    timeline.Position;


                if (position < TimeSpan.Zero)
                    position =
                        TimeSpan.Zero;


                if (position > currentDuration)
                    position =
                        currentDuration;


                currentPosition =
                    position;


                lastApiPosition =
                    position;


                hasApiPosition = true;


                positionStartedAt =
                    DateTime.UtcNow;


                if (!isDraggingSlider)
                {
                    UpdateVisualPosition(
                        currentPosition);
                }
            }
            catch
            {
            }
        }


        // =========================================================
        // ДВИЖЕНИЕ ВРЕМЕНИ
        // =========================================================

        private void UpdateDisplayedPosition()
        {
            if (currentDuration.TotalSeconds <= 0)
                return;


            if (isDraggingSlider)
                return;


            TimeSpan position =
                currentPosition;


            if (isPlaying)
            {
                position +=
                    DateTime.UtcNow -
                    positionStartedAt;


                if (position >
                    currentDuration)
                {
                    position =
                        currentDuration;
                }
            }


            UpdateVisualPosition(
                position);
        }


        // =========================================================
        // ОТРИСОВКА ПОЛЗУНКА
        // =========================================================

        private void UpdateVisualPosition(
            TimeSpan position)
        {
            if (currentDuration.TotalSeconds <= 0)
                return;


            double value =
                position.TotalSeconds /
                currentDuration.TotalSeconds;


            value =
                Math.Max(
                    0,
                    Math.Min(
                        1,
                        value));


            CurrentTimeText.Text =
                FormatTime(position);


            DurationText.Text =
                FormatTime(currentDuration);


            ProgressSlider.Value =
                value;


            UpdateProgressVisual(
                value);
        }


        private void UpdateProgressVisual(
            double value)
        {
            if (ProgressArea.ActualWidth <= 0)
                return;


            double width =
                ProgressArea.ActualWidth *
                value;


            ProgressFill.Width =
                Math.Max(
                    0,
                    width);


            double thumbX =
                width -
                ProgressThumb.Width / 2;


            thumbX =
                Math.Max(
                    0,
                    Math.Min(
                        ProgressArea.ActualWidth -
                        ProgressThumb.Width,
                        thumbX));


            ProgressThumb.Margin =
                new Thickness(
                    thumbX,
                    0,
                    0,
                    0);
        }


        // =========================================================
        // НАВЕДЕНИЕ
        // =========================================================

        private void ProgressArea_MouseEnter(
            object sender,
            MouseEventArgs e)
        {
            isMouseOverProgress = true;

            ProgressThumb.Visibility =
                Visibility.Visible;
        }


        private void ProgressArea_MouseLeave(
            object sender,
            MouseEventArgs e)
        {
            if (!isDraggingSlider)
            {
                isMouseOverProgress = false;

                ProgressThumb.Visibility =
                    Visibility.Collapsed;
            }
        }


        // =========================================================
        // НАЖАТИЕ ПОЛЗУНКА
        // =========================================================

        private void ProgressSlider_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (currentSession == null)
                return;


            ShowOverlay();


            isDraggingSlider = true;


            ProgressThumb.Visibility =
                Visibility.Visible;


            SetSliderFromMouse(e);


            ProgressSlider.CaptureMouse();


            e.Handled = true;
        }


        // =========================================================
        // ДВИЖЕНИЕ ПОЛЗУНКА
        // =========================================================

        private void ProgressSlider_PreviewMouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (!isDraggingSlider)
                return;


            ShowOverlay();


            SetSliderFromMouse(e);


            e.Handled = true;
        }


        // =========================================================
        // ОТПУСКАНИЕ ПОЛЗУНКА
        // =========================================================

        private async void ProgressSlider_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!isDraggingSlider)
                return;


            SetSliderFromMouse(e);


            isDraggingSlider = false;


            ProgressSlider.ReleaseMouseCapture();


            await SeekToPosition();


            if (!isMouseOverProgress)
            {
                ProgressThumb.Visibility =
                    Visibility.Collapsed;
            }


            ShowOverlay();


            e.Handled = true;
        }


        // =========================================================
        // ПОЗИЦИЯ МЫШИ
        // =========================================================

        private void SetSliderFromMouse(
            MouseEventArgs e)
        {
            Point point =
                e.GetPosition(
                    ProgressArea);


            double width =
                ProgressArea.ActualWidth;


            if (width <= 0)
                return;


            double value =
                point.X / width;


            value =
                Math.Max(
                    0,
                    Math.Min(
                        1,
                        value));


            ProgressSlider.Value =
                value;


            UpdateProgressVisual(
                value);


            UpdateTimeFromSlider(
                value);
        }


        private void UpdateTimeFromSlider(
            double value)
        {
            if (currentDuration.TotalSeconds <= 0)
                return;


            TimeSpan newPosition =
                TimeSpan.FromSeconds(
                    currentDuration.TotalSeconds *
                    value);


            CurrentTimeText.Text =
                FormatTime(newPosition);


            DurationText.Text =
                FormatTime(currentDuration);
        }


        // =========================================================
        // ПЕРЕМОТКА
        // =========================================================

        private async System.Threading.Tasks.Task SeekToPosition()
        {
            if (currentSession == null)
                return;


            try
            {
                TimeSpan newPosition =
                    TimeSpan.FromSeconds(
                        currentDuration.TotalSeconds *
                        ProgressSlider.Value);


                await currentSession
                    .TryChangePlaybackPositionAsync(
                        newPosition.Ticks);


                currentPosition =
                    newPosition;


                lastApiPosition =
                    newPosition;


                positionStartedAt =
                    DateTime.UtcNow;


                UpdateVisualPosition(
                    newPosition);
            }
            catch
            {
            }
        }


        // =========================================================
        // PLAY / PAUSE
        // =========================================================

        private async void PlayPauseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (currentSession == null)
                return;


            try
            {
                ShowOverlay();


                var playbackInfo =
                    currentSession.GetPlaybackInfo();


                if (playbackInfo == null)
                    return;


                bool currentlyPlaying =
                    playbackInfo.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;


                if (currentlyPlaying)
                {
                    UpdateDisplayedPosition();


                    currentPosition =
                        TimeSpan.FromSeconds(
                            ProgressSlider.Value *
                            currentDuration.TotalSeconds);


                    await currentSession
                        .TryPauseAsync();


                    isPlaying = false;
                }
                else
                {
                    await currentSession
                        .TryPlayAsync();


                    isPlaying = true;


                    positionStartedAt =
                        DateTime.UtcNow;
                }


                await System.Threading.Tasks.Task.Delay(150);


                UpdatePlayPauseIcon();


                ShowOverlay();
            }
            catch
            {
            }
        }


        // =========================================================
        // ИКОНКА PLAY / PAUSE
        // =========================================================

        private void UpdatePlayPauseIcon()
        {
            if (isPlaying)
                SetPauseIcon();
            else
                SetPlayIcon();
        }


        private void SetPlayIcon()
        {
            PlayIcon.Data =
                Geometry.Parse(
                    "M 0,0 L 0,20 L 16,10 Z");
        }


        private void SetPauseIcon()
        {
            PlayIcon.Data =
                Geometry.Parse(
                    "M 0,0 L 6,0 L 6,20 L 0,20 Z " +
                    "M 10,0 L 16,0 L 16,20 L 10,20 Z");
        }


        // =========================================================
        // КНОПКА НАЗАД
        // =========================================================

        private async void PreviousButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (currentSession == null)
                return;


            try
            {
                manualTrackChange = true;

                ShowOverlay();


                await currentSession
                    .TrySkipPreviousAsync();


                await System.Threading.Tasks.Task.Delay(400);


                hasApiPosition = false;

                lastApiPosition =
                    TimeSpan.MinValue;


                await UpdateSongInfo();

                SyncPosition();

                UpdatePlaybackState();


                ShowOverlay();
            }
            catch
            {
                manualTrackChange = false;
            }
        }


        // =========================================================
        // КНОПКА ВПЕРЁД
        // =========================================================

        private async void NextButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (currentSession == null)
                return;


            try
            {
                manualTrackChange = true;

                ShowOverlay();


                await currentSession
                    .TrySkipNextAsync();


                await System.Threading.Tasks.Task.Delay(400);


                hasApiPosition = false;

                lastApiPosition =
                    TimeSpan.MinValue;


                await UpdateSongInfo();

                SyncPosition();

                UpdatePlaybackState();


                ShowOverlay();
            }
            catch
            {
                manualTrackChange = false;
            }
        }


        // =========================================================
        // ФОРМАТ ВРЕМЕНИ
        // =========================================================

        private string FormatTime(
            TimeSpan time)
        {
            if (time.TotalHours >= 1)
            {
                return time.ToString(
                    @"h\:mm\:ss");
            }


            return time.ToString(
                @"m\:ss");
        }
    }
}