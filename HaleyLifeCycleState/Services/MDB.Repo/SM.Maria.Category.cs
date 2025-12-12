using Haley.Abstractions;
using Haley.Internal;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB {

        public Task<IFeedback<Dictionary<string, object>>> InsertCategoryAsync(string displayName) =>
            _agw.ReadSingleAsync(_key, QRY_CATEGORY.INSERT, (DISPLAY_NAME, displayName));

        public Task<IFeedback<List<Dictionary<string, object>>>> GetAllCategoriesAsync() =>
            _agw.ReadAsync(_key, QRY_CATEGORY.GET_ALL);

        public Task<IFeedback<Dictionary<string, object>>> GetCategoryByNameAsync(string name) =>
            _agw.ReadSingleAsync(_key, QRY_CATEGORY.GET_BY_NAME, (NAME, name));
    }
}
