using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Service.Interfaces
{
    public interface IQueryTokenizer
    {
        //Split on whitespace/.,, (regex: [,\.\s]+) – returns RAW tokens (keep case/diacritics).
        IReadOnlyList<string> Split(string query);

        //Lowercase + unaccent (deterministic, matches DB unaccent).
        string Normalize(string token);
    }
}
