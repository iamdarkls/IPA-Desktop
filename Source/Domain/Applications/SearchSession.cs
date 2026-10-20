namespace IpaDesktop
{
    /// <summary>Ревизия поискового запроса: устаревшие асинхронные ответы не возвращаются в выдачу.</summary>
    public class SearchSession
    {
        int revision;
        string query = "";
        /// <summary>Повышает ревизию запроса, чтобы игнорировать ответы прежнего поиска.</summary>
        public int Change(string value)
        {
            query = (value ?? "").Trim();
            return++revision;
        }
        public int Revision
        {
            get
            {
                return revision;
            }
        }
        /// <summary>Проверяет, относится ли асинхронный ответ к текущему запросу.</summary>
        public bool IsCurrent(int request)
        {
            return request == revision && query.Length > 0;
        }
    }
}
