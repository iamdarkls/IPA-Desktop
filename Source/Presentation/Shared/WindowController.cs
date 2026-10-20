using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

namespace IpaDesktop
{
    /// <summary>Общий доступ контроллеров к окну, рабочей операции, журналу и обновлению состояния действий.</summary>
    public abstract class WindowController
    {
        protected readonly Desktop view;
        protected readonly Engine engine;
        protected Window w
        {
            get
            {
                return view.Window;
            }
        }
        protected bool busy
        {
            get
            {
                return view.IsBusy;
            }
        }
        protected bool setting
        {
            get
            {
                return view.IsUpdatingSettings;
            }
        }
        protected WindowController(Desktop view, Engine engine)
        {
            this.view = view;
            this.engine = engine;
        }
        protected T C<T>(string name) where T : class
        {
            return view.C<T>(name);
        }
        /// <summary>Подключает действие к кнопке и передаёт исключения в общий обработчик интерфейса.</summary>
        protected void On(string name, Action action)
        {
            view.On(name, action);
        }
        /// <summary>Не допускает параллельных операций, управляет отменой и восстанавливает UI в finally.</summary>
        protected void Work(string title, Func<CancellationToken, Task> action)
        {
            view.Work(title, action);
        }
        /// <summary>Передаёт метрики прогрессу, а обычные сообщения — в ограниченный буфер журнала.</summary>
        protected void Log(string text)
        {
            view.Log(text);
        }
        /// <summary>Показывает ошибку пользователю и сохраняет сообщение в журнале.</summary>
        protected void Error(Exception error)
        {
            view.Error(error);
        }
        /// <summary>Обновляет состояния всех созданных контроллеров; допускает частичную инициализацию окна.</summary>
        protected void ApplyActions()
        {
            view.ApplyActions();
        }
        /// <summary>Отображает настройки, блокируя повторную реакцию обработчиков переключателей.</summary>
        protected void StateView()
        {
            view.StateView();
        }
        protected void RefreshLocalFiles()
        {
            view.RefreshFiles();
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public abstract void RefreshActions();
    }
}
