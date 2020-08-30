using System.Collections.Generic;

using MovieList.Data.Models;

namespace MovieList.Data.Services
{
    public interface IListService
    {
        MovieList GetList(IEnumerable<Kind> kinds, IEnumerable<Tag> tags);
    }
}
