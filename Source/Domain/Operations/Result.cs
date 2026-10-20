namespace IpaDesktop
{
    /// <summary>Код завершения, стандартный вывод и ошибки одной команды внешней утилиты.</summary>
    public class Result
    {
        public int Code
        {
            get;
            set;
        }
        public string Output
        {
            get;
            set;
        }
    }
}
