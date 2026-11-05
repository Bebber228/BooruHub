using System;
using System.Diagnostics;
using System.Resources;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using ArtHub.Resources;

namespace ArtHub
{
    public partial class App : Application
    {
        /// <summary>
        /// Обеспечивает быстрый доступ к корневому кадру приложения телефона.
        /// </summary>
        /// <returns>Корневой кадр приложения телефона.</returns>
        public static PhoneApplicationFrame RootFrame { get; private set; }

        /// <summary>
        /// Конструктор объекта приложения.
        /// </summary>
        public App()
        {
            // Глобальный обработчик неперехваченных исключений.
            UnhandledException += Application_UnhandledException;

            // Стандартная инициализация XAML
            InitializeComponent();

            // Инициализация телефона
            InitializePhoneApplication();

            // Инициализация отображения языка
            InitializeLanguage();

            // Отображение сведений о профиле графики во время отладки.
            if (Debugger.IsAttached)
            {
                // Отображение текущих счетчиков частоты смены кадров.
                Application.Current.Host.Settings.EnableFrameRateCounter = true;

                // Отображение областей приложения, перерисовываемых в каждом кадре.
                //Application.Current.Host.Settings.EnableRedrawRegions = true;

                // Включение режима визуализации анализа нерабочего кода,
                // для отображения областей страницы, переданных в GPU, с цветным наложением.
                //Application.Current.Host.Settings.EnableCacheVisualization = true;

                // Предотвратить выключение экрана в режиме отладчика путем отключения
                // определения состояния простоя приложения.
                // Внимание! Используйте только в режиме отладки. Приложение, в котором отключено обнаружение бездействия пользователя, будет продолжать работать
                // и потреблять энергию батареи, когда телефон не будет использоваться.
                PhoneApplicationService.Current.UserIdleDetectionMode = IdleDetectionMode.Disabled;
            }

        }

        // Код для выполнения при запуске приложения (например, из меню "Пуск")
        // Этот код не будет выполняться при повторной активации приложения
        private void Application_Launching(object sender, LaunchingEventArgs e)
        {
            Settings.ClearSession();
            Tiles.Apply(Settings.TransparentTile);
        }

        // Код для выполнения при активации приложения (переводится в основной режим)
        // Этот код не будет выполняться при первом запуске приложения
        private void Application_Activated(object sender, ActivatedEventArgs e)
        {
        }

        // Код для выполнения при деактивации приложения (отправляется в фоновый режим)
        // Этот код не будет выполняться при закрытии приложения
        private void Application_Deactivated(object sender, DeactivatedEventArgs e)
        {
        }

        // Код для выполнения при закрытии приложения (например, при нажатии пользователем кнопки "Назад")
        // Этот код не будет выполняться при деактивации приложения
        private void Application_Closing(object sender, ClosingEventArgs e)
        {
            Settings.ClearSession();
        }

        // Код для выполнения в случае ошибки навигации
        private void RootFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            e.Handled = true;
            MessageBox.Show(e.Uri + "\n\n" + e.Exception);
        }

        // Код для выполнения на необработанных исключениях
        private void Application_UnhandledException(object sender, ApplicationUnhandledExceptionEventArgs e)
        {
            // не даём приложению закрыться из-за сетевых и прочих сбоев в фоне
            e.Handled = true;
        }

        #region Инициализация приложения телефона

        // Избегайте двойной инициализации
        private bool phoneApplicationInitialized = false;

        // Не добавляйте в этот метод дополнительный код
        private void InitializePhoneApplication()
        {
            if (phoneApplicationInitialized)
                return;

            // сначала создаём фрейм
            RootFrame = new TransitionFrame();
            RootFrame.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(255, 0xCE, 0xD9, 0xCC));


            RootFrame.Navigated += CompleteInitializePhoneApplication;
            RootFrame.NavigationFailed += RootFrame_NavigationFailed;
            RootFrame.Navigated += CheckForResetNavigation;

            phoneApplicationInitialized = true;
        }

        // Не добавляйте в этот метод дополнительный код
        private void CompleteInitializePhoneApplication(object sender, NavigationEventArgs e)
        {
            // Задайте корневой визуальный элемент для визуализации приложения
            if (RootVisual != RootFrame)
                RootVisual = RootFrame;

            // Удалите этот обработчик, т.к. он больше не нужен
            RootFrame.Navigated -= CompleteInitializePhoneApplication;
        }

        private void CheckForResetNavigation(object sender, NavigationEventArgs e)
        {
            // Если приложение получило навигацию "reset", необходимо проверить
            // при следующей навигации, чтобы проверить, нужно ли выполнять сброс стека
            if (e.NavigationMode == NavigationMode.Reset)
                RootFrame.Navigated += ClearBackStackAfterReset;
        }

        private void ClearBackStackAfterReset(object sender, NavigationEventArgs e)
        {
            // Отменить регистрацию события, чтобы оно больше не вызывалось
            RootFrame.Navigated -= ClearBackStackAfterReset;

            // Очистка стека только для "новых" навигаций (вперед) и навигаций "обновления"
            if (e.NavigationMode != NavigationMode.New && e.NavigationMode != NavigationMode.Refresh)
                return;

            // Очистка всего стека страницы для согласованности пользовательского интерфейса
            while (RootFrame.RemoveBackEntry() != null)
            {
                ; // ничего не делать
            }
        }

        #endregion

        // Инициализация шрифта приложения и направления текста, как определено в локализованных строках ресурсов.
        //
        // Чтобы убедиться, что шрифт приложения соответствует поддерживаемым языкам, а
        // FlowDirection для каждого из этих языков соответствует традиционному направлению, ResourceLanguage
        // и ResourceFlowDirection необходимо инициализировать в каждом RESX-файле, чтобы эти значения совпадали с
        // культурой файла. Пример:
        //
        // AppResources.es-ES.resx
        //    Значение ResourceLanguage должно равняться "es-ES"
        //    Значение ResourceFlowDirection должно равняться "LeftToRight"
        //
        // AppResources.ar-SA.resx
        //     Значение ResourceLanguage должно равняться "ar-SA"
        //     Значение ResourceFlowDirection должно равняться "RightToLeft"
        //
        // Дополнительные сведения о локализации приложений Windows Phone см. на странице http://go.microsoft.com/fwlink/?LinkId=262072.
        //
        private void InitializeLanguage()
        {
            try
            {
                // Задать шрифт в соответствии с отображаемым языком, определенным
                // строкой ресурса ResourceLanguage для каждого поддерживаемого языка.
                //
                // Откат к шрифту нейтрального языка, если отображаемый
                // язык телефона не поддерживается.
                //
                // Если возникла ошибка компилятора, ResourceLanguage отсутствует в
                // файл ресурсов.
                RootFrame.Language = XmlLanguage.GetLanguage(AppResources.ResourceLanguage);

                // Установка FlowDirection для всех элементов в корневом кадре на основании
                // строки ресурса ResourceFlowDirection для каждого
                // поддерживаемого языка.
                //
                // Если возникла ошибка компилятора, ResourceFlowDirection отсутствует в
                // файл ресурсов.
                FlowDirection flow = (FlowDirection)Enum.Parse(typeof(FlowDirection), AppResources.ResourceFlowDirection);
                RootFrame.FlowDirection = flow;
            }
            catch
            {
                // Если здесь перехвачено исключение, вероятнее всего это означает, что
                // для ResourceLangauge неправильно задан код поддерживаемого языка
                // или для ResourceFlowDirection задано значение, отличное от LeftToRight
                // или RightToLeft.

                if (Debugger.IsAttached)
                {
                    Debugger.Break();
                }

                throw;
            }
        }
    }
}