using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Server.Implementations.Persistence
{
    /// <summary>
    /// Class SQLiteItemRepository
    /// </summary>
    public class SqliteItemRepository : IItemRepository
    {
        private IDbConnection _connection;

        private readonly ILogger _logger;

        private readonly TypeMapper _typeMapper = new TypeMapper();

        /// <summary>
        /// Gets the name of the repository
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get
            {
                return "SQLite";
            }
        }

        /// <summary>
        /// Gets the json serializer.
        /// </summary>
        /// <value>The json serializer.</value>
        private readonly IJsonSerializer _jsonSerializer;

        /// <summary>
        /// The _app paths
        /// </summary>
        private readonly IApplicationPaths _appPaths;

        /// <summary>
        /// The _save item command
        /// </summary>
        private IDbCommand _saveItemCommand;

        private readonly string _criticReviewsPath;

        private SqliteChapterRepository _chapterRepository;
        private SqliteMediaStreamsRepository _mediaStreamsRepository;

        private IDbCommand _deleteChildrenCommand;
        private IDbCommand _saveChildrenCommand;
        private IDbCommand _deleteItemCommand;

        private IDbCommand _deletePeopleCommand;
        private IDbCommand _savePersonCommand;

        private const int LatestSchemaVersion = 6;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteItemRepository"/> class.
        /// </summary>
        /// <param name="appPaths">The app paths.</param>
        /// <param name="jsonSerializer">The json serializer.</param>
        /// <param name="logManager">The log manager.</param>
        /// <exception cref="System.ArgumentNullException">
        /// appPaths
        /// or
        /// jsonSerializer
        /// </exception>
        public SqliteItemRepository(IApplicationPaths appPaths, IJsonSerializer jsonSerializer, ILogManager logManager)
        {
            if (appPaths == null)
            {
                throw new ArgumentNullException("appPaths");
            }
            if (jsonSerializer == null)
            {
                throw new ArgumentNullException("jsonSerializer");
            }

            _appPaths = appPaths;
            _jsonSerializer = jsonSerializer;

            _criticReviewsPath = Path.Combine(_appPaths.DataPath, "critic-reviews");

            _logger = logManager.GetLogger(GetType().Name);

            var chapterDbFile = Path.Combine(_appPaths.DataPath, "chapters.db");
            var chapterConnection = SqliteExtensions.ConnectToDb(chapterDbFile, _logger).Result;
            _chapterRepository = new SqliteChapterRepository(chapterConnection, logManager);

            var mediaStreamsDbFile = Path.Combine(_appPaths.DataPath, "mediainfo.db");
            var mediaStreamsConnection = SqliteExtensions.ConnectToDb(mediaStreamsDbFile, _logger).Result;
            _mediaStreamsRepository = new SqliteMediaStreamsRepository(mediaStreamsConnection, logManager);
        }

        /// <summary>
        /// Opens the connection to the database
        /// </summary>
        /// <returns>Task.</returns>
        public async Task Initialize()
        {
            var dbFile = Path.Combine(_appPaths.DataPath, "library.db");

            _connection = await SqliteExtensions.ConnectToDb(dbFile, _logger).ConfigureAwait(false);

            string[] queries = {

                                "create table if not exists TypedBaseItems (guid GUID primary key, type TEXT, data BLOB)",
                                "create index if not exists idx_TypedBaseItems on TypedBaseItems(guid)",

                                "create table if not exists ChildrenIds (ParentId GUID, ItemId GUID, PRIMARY KEY (ParentId, ItemId))",
                                "create index if not exists idx_ChildrenIds on ChildrenIds(ParentId,ItemId)",

                                "create table if not exists People (ItemId GUID, Name TEXT NOT NULL, Role TEXT, PersonType TEXT, SortOrder int, ListOrder int)",

                                //pragmas
                                "pragma temp_store = memory",

                                "pragma shrink_memory"
                               };

            _connection.RunQueries(queries, _logger);

            _connection.AddColumn(_logger, "TypedBaseItems", "Path", "Text");
            _connection.AddColumn(_logger, "TypedBaseItems", "StartDate", "DATETIME");
            _connection.AddColumn(_logger, "TypedBaseItems", "EndDate", "DATETIME");
            _connection.AddColumn(_logger, "TypedBaseItems", "ChannelId", "Text");
            _connection.AddColumn(_logger, "TypedBaseItems", "IsMovie", "BIT");
            _connection.AddColumn(_logger, "TypedBaseItems", "IsSports", "BIT");
            _connection.AddColumn(_logger, "TypedBaseItems", "IsKids", "BIT");
            _connection.AddColumn(_logger, "TypedBaseItems", "CommunityRating", "Float");
            _connection.AddColumn(_logger, "TypedBaseItems", "CustomRating", "Text");
            _connection.AddColumn(_logger, "TypedBaseItems", "IndexNumber", "INT");
            _connection.AddColumn(_logger, "TypedBaseItems", "IsLocked", "BIT");
            _connection.AddColumn(_logger, "TypedBaseItems", "Name", "Text");
            _connection.AddColumn(_logger, "TypedBaseItems", "OfficialRating", "Text");

            _connection.AddColumn(_logger, "TypedBaseItems", "MediaType", "Text");
            _connection.AddColumn(_logger, "TypedBaseItems", "Overview", "Text");
            _connection.AddColumn(_logger, "TypedBaseItems", "ParentIndexNumber", "INT");
            _connection.AddColumn(_logger, "TypedBaseItems", "PremiereDate", "DATETIME");
            _connection.AddColumn(_logger, "TypedBaseItems", "ProductionYear", "INT");
            _connection.AddColumn(_logger, "TypedBaseItems", "ParentId", "GUID");
            _connection.AddColumn(_logger, "TypedBaseItems", "Genres", "Text");
            _connection.AddColumn(_logger, "TypedBaseItems", "ParentalRatingValue", "INT");
            _connection.AddColumn(_logger, "TypedBaseItems", "SchemaVersion", "INT");
            _connection.AddColumn(_logger, "TypedBaseItems", "SortName", "Text");
            _connection.AddColumn(_logger, "TypedBaseItems", "RunTimeTicks", "BIGINT");

            _connection.AddColumn(_logger, "TypedBaseItems", "OfficialRatingDescription", "Text");
            _connection.AddColumn(_logger, "TypedBaseItems", "HomePageUrl", "Text");
            _connection.AddColumn(_logger, "TypedBaseItems", "VoteCount", "INT");
            _connection.AddColumn(_logger, "TypedBaseItems", "DisplayMediaType", "Text");
            _connection.AddColumn(_logger, "TypedBaseItems", "DateCreated", "DATETIME");
            _connection.AddColumn(_logger, "TypedBaseItems", "DateModified", "DATETIME");

            _connection.AddColumn(_logger, "TypedBaseItems", "ForcedSortName", "Text");
            _connection.AddColumn(_logger, "TypedBaseItems", "IsOffline", "BIT");

            PrepareStatements();

            _mediaStreamsRepository.Initialize();
            _chapterRepository.Initialize();
        }

        /// <summary>
        /// The _write lock
        /// </summary>
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        private string[] _retriveItemColumns =
        {
            "type",
            "data",
            "IsOffline"
        };

        /// <summary>
        /// Prepares the statements.
        /// </summary>
        private void PrepareStatements()
        {
            var saveColumns = new List<string>
            {
                "guid",
                "type",
                "data",
				"Path",
                "StartDate",
                "EndDate",
                "ChannelId",
                "IsKids",
                "IsMovie",
                "IsSports",
                "CommunityRating",
                "CustomRating",
                "IndexNumber",
                "IsLocked",
                "Name",
                "OfficialRating",
                "MediaType",
                "Overview",
                "ParentIndexNumber",
                "PremiereDate",
                "ProductionYear",
                "ParentId",
                "Genres",
                "ParentalRatingValue",
                "SchemaVersion",
                "SortName",
                "RunTimeTicks",
                "OfficialRatingDescription",
                "HomePageUrl",
                "VoteCount",
                "DisplayMediaType",
                "DateCreated",
                "DateModified",
                "ForcedSortName",
                "IsOffline"
            };
            _saveItemCommand = _connection.CreateCommand();
            _saveItemCommand.CommandText = "replace into TypedBaseItems (" + string.Join(",", saveColumns.ToArray()) + ") values (";

            for (var i = 1; i <= saveColumns.Count; i++)
            {
                if (i > 1)
                {
                    _saveItemCommand.CommandText += ",";
                }
                _saveItemCommand.CommandText += "@" + i.ToString(CultureInfo.InvariantCulture);

                _saveItemCommand.Parameters.Add(_saveItemCommand, "@" + i.ToString(CultureInfo.InvariantCulture));
            }
            _saveItemCommand.CommandText += ")";

            _deleteChildrenCommand = _connection.CreateCommand();
            _deleteChildrenCommand.CommandText = "delete from ChildrenIds where ParentId=@ParentId";
            _deleteChildrenCommand.Parameters.Add(_deleteChildrenCommand, "@ParentId");

            _deleteItemCommand = _connection.CreateCommand();
            _deleteItemCommand.CommandText = "delete from TypedBaseItems where guid=@Id";
            _deleteItemCommand.Parameters.Add(_deleteItemCommand, "@Id");

            _saveChildrenCommand = _connection.CreateCommand();
            _saveChildrenCommand.CommandText = "replace into ChildrenIds (ParentId, ItemId) values (@ParentId, @ItemId)";
            _saveChildrenCommand.Parameters.Add(_saveChildrenCommand, "@ParentId");
            _saveChildrenCommand.Parameters.Add(_saveChildrenCommand, "@ItemId");

            _deletePeopleCommand = _connection.CreateCommand();
            _deletePeopleCommand.CommandText = "delete from People where ItemId=@Id";
            _deletePeopleCommand.Parameters.Add(_deletePeopleCommand, "@Id");

            _savePersonCommand = _connection.CreateCommand();
            _savePersonCommand.CommandText = "insert into People (ItemId, Name, Role, PersonType, SortOrder, ListOrder) values (@ItemId, @Name, @Role, @PersonType, @SortOrder, @ListOrder)";
            _savePersonCommand.Parameters.Add(_savePersonCommand, "@ItemId");
            _savePersonCommand.Parameters.Add(_savePersonCommand, "@Name");
            _savePersonCommand.Parameters.Add(_savePersonCommand, "@Role");
            _savePersonCommand.Parameters.Add(_savePersonCommand, "@PersonType");
            _savePersonCommand.Parameters.Add(_savePersonCommand, "@SortOrder");
            _savePersonCommand.Parameters.Add(_savePersonCommand, "@ListOrder");
        }

        /// <summary>
        /// Save a standard item in the repo
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.ArgumentNullException">item</exception>
        public Task SaveItem(BaseItem item, CancellationToken cancellationToken)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            return SaveItems(new[] { item }, cancellationToken);
        }

        /// <summary>
        /// Saves the items.
        /// </summary>
        /// <param name="items">The items.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// items
        /// or
        /// cancellationToken
        /// </exception>
        public async Task SaveItems(IEnumerable<BaseItem> items, CancellationToken cancellationToken)
        {
            if (items == null)
            {
                throw new ArgumentNullException("items");
            }

            cancellationToken.ThrowIfCancellationRequested();

            CheckDisposed();

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            IDbTransaction transaction = null;

            try
            {
                transaction = _connection.BeginTransaction();

                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var index = 0;

                    _saveItemCommand.GetParameter(index++).Value = item.Id;
                    _saveItemCommand.GetParameter(index++).Value = item.GetType().FullName;
                    _saveItemCommand.GetParameter(index++).Value = _jsonSerializer.SerializeToBytes(item);

                    _saveItemCommand.GetParameter(index++).Value = item.Path;

                    var hasStartDate = item as IHasStartDate;
                    if (hasStartDate != null)
                    {
                        _saveItemCommand.GetParameter(index++).Value = hasStartDate.StartDate;
                    }
                    else
                    {
                        _saveItemCommand.GetParameter(index++).Value = null;
                    }

                    _saveItemCommand.GetParameter(index++).Value = item.EndDate;
                    _saveItemCommand.GetParameter(index++).Value = item.ChannelId;

                    var hasProgramAttributes = item as IHasProgramAttributes;
                    if (hasProgramAttributes != null)
                    {
                        _saveItemCommand.GetParameter(index++).Value = hasProgramAttributes.IsKids;
                        _saveItemCommand.GetParameter(index++).Value = hasProgramAttributes.IsMovie;
                        _saveItemCommand.GetParameter(index++).Value = hasProgramAttributes.IsSports;
                    }
                    else
                    {
                        _saveItemCommand.GetParameter(index++).Value = null;
                        _saveItemCommand.GetParameter(index++).Value = null;
                        _saveItemCommand.GetParameter(index++).Value = null;
                    }

                    _saveItemCommand.GetParameter(index++).Value = item.CommunityRating;
                    _saveItemCommand.GetParameter(index++).Value = item.CustomRating;

                    _saveItemCommand.GetParameter(index++).Value = item.IndexNumber;
                    _saveItemCommand.GetParameter(index++).Value = item.IsLocked;

                    _saveItemCommand.GetParameter(index++).Value = item.Name;
                    _saveItemCommand.GetParameter(index++).Value = item.OfficialRating;

                    _saveItemCommand.GetParameter(index++).Value = item.MediaType;
                    _saveItemCommand.GetParameter(index++).Value = item.Overview;
                    _saveItemCommand.GetParameter(index++).Value = item.ParentIndexNumber;
                    _saveItemCommand.GetParameter(index++).Value = item.PremiereDate;
                    _saveItemCommand.GetParameter(index++).Value = item.ProductionYear;

                    if (item.ParentId == Guid.Empty)
                    {
                        _saveItemCommand.GetParameter(index++).Value = null;
                    }
                    else
                    {
                        _saveItemCommand.GetParameter(index++).Value = item.ParentId;
                    }

                    _saveItemCommand.GetParameter(index++).Value = string.Join("|", item.Genres.ToArray());
                    _saveItemCommand.GetParameter(index++).Value = item.GetParentalRatingValue();

                    _saveItemCommand.GetParameter(index++).Value = LatestSchemaVersion;
                    _saveItemCommand.GetParameter(index++).Value = item.SortName;
                    _saveItemCommand.GetParameter(index++).Value = item.RunTimeTicks;

                    _saveItemCommand.GetParameter(index++).Value = item.OfficialRatingDescription;
                    _saveItemCommand.GetParameter(index++).Value = item.HomePageUrl;
                    _saveItemCommand.GetParameter(index++).Value = item.VoteCount;
                    _saveItemCommand.GetParameter(index++).Value = item.DisplayMediaType;
                    _saveItemCommand.GetParameter(index++).Value = item.DateCreated;
                    _saveItemCommand.GetParameter(index++).Value = item.DateModified;

                    _saveItemCommand.GetParameter(index++).Value = item.ForcedSortName;
                    _saveItemCommand.GetParameter(index++).Value = item.IsOffline;

                    _saveItemCommand.Transaction = transaction;

                    _saveItemCommand.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch (OperationCanceledException)
            {
                if (transaction != null)
                {
                    transaction.Rollback();
                }

                throw;
            }
            catch (Exception e)
            {
                _logger.ErrorException("Failed to save items:", e);

                if (transaction != null)
                {
                    transaction.Rollback();
                }

                throw;
            }
            finally
            {
                if (transaction != null)
                {
                    transaction.Dispose();
                }

                _writeLock.Release();
            }
        }

        /// <summary>
        /// Internal retrieve from items or users table
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>BaseItem.</returns>
        /// <exception cref="System.ArgumentNullException">id</exception>
        /// <exception cref="System.ArgumentException"></exception>
        public BaseItem RetrieveItem(Guid id)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentNullException("id");
            }

            CheckDisposed();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "select " + string.Join(",", _retriveItemColumns) + " from TypedBaseItems where guid = @guid";
                cmd.Parameters.Add(cmd, "@guid", DbType.Guid).Value = id;

                using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult | CommandBehavior.SingleRow))
                {
                    if (reader.Read())
                    {
                        return GetItem(reader);
                    }
                }
                return null;
            }
        }

        private BaseItem GetItem(IDataReader reader)
        {
            var typeString = reader.GetString(0);

            var type = _typeMapper.GetType(typeString);

            if (type == null)
            {
                _logger.Debug("Unknown type {0}", typeString);

                return null;
            }

            BaseItem item;

            using (var stream = reader.GetMemoryStream(1))
            {
                try
                {
                    item = _jsonSerializer.DeserializeFromStream(stream, type) as BaseItem;

                    if (item == null)
                    {
                        return null;
                    }
                }
                catch (SerializationException ex)
                {
                    _logger.ErrorException("Error deserializing item", ex);
                    return null;
                }
            }

            if (!reader.IsDBNull(2))
            {
                item.IsOffline = reader.GetBoolean(2);
            }

            return item;
        }

        /// <summary>
        /// Gets the critic reviews.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <returns>Task{IEnumerable{ItemReview}}.</returns>
        public IEnumerable<ItemReview> GetCriticReviews(Guid itemId)
        {
            try
            {
                var path = Path.Combine(_criticReviewsPath, itemId + ".json");

                return _jsonSerializer.DeserializeFromFile<List<ItemReview>>(path);
            }
            catch (DirectoryNotFoundException)
            {
                return new List<ItemReview>();
            }
            catch (FileNotFoundException)
            {
                return new List<ItemReview>();
            }
        }

        private readonly Task _cachedTask = Task.FromResult(true);
        /// <summary>
        /// Saves the critic reviews.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="criticReviews">The critic reviews.</param>
        /// <returns>Task.</returns>
        public Task SaveCriticReviews(Guid itemId, IEnumerable<ItemReview> criticReviews)
        {
            Directory.CreateDirectory(_criticReviewsPath);

            var path = Path.Combine(_criticReviewsPath, itemId + ".json");

            _jsonSerializer.SerializeToFile(criticReviews.ToList(), path);

            return _cachedTask;
        }

        /// <summary>
        /// Gets chapters for an item
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>IEnumerable{ChapterInfo}.</returns>
        /// <exception cref="System.ArgumentNullException">id</exception>
        public IEnumerable<ChapterInfo> GetChapters(Guid id)
        {
            CheckDisposed();
            return _chapterRepository.GetChapters(id);
        }

        /// <summary>
        /// Gets a single chapter for an item
        /// </summary>
        /// <param name="id">The id.</param>
        /// <param name="index">The index.</param>
        /// <returns>ChapterInfo.</returns>
        /// <exception cref="System.ArgumentNullException">id</exception>
        public ChapterInfo GetChapter(Guid id, int index)
        {
            CheckDisposed();
            return _chapterRepository.GetChapter(id, index);
        }

        /// <summary>
        /// Saves the chapters.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <param name="chapters">The chapters.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// id
        /// or
        /// chapters
        /// or
        /// cancellationToken
        /// </exception>
        public Task SaveChapters(Guid id, IEnumerable<ChapterInfo> chapters, CancellationToken cancellationToken)
        {
            CheckDisposed();
            return _chapterRepository.SaveChapters(id, chapters, cancellationToken);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private readonly object _disposeLock = new object();

        private bool _disposed;
        private void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name + " has been disposed and cannot be accessed.");
            }
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="dispose"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool dispose)
        {
            if (dispose)
            {
                _disposed = true;

                try
                {
                    lock (_disposeLock)
                    {
                        _writeLock.Wait();

                        if (_connection != null)
                        {
                            if (_connection.IsOpen())
                            {
                                _connection.Close();
                            }

                            _connection.Dispose();
                            _connection = null;
                        }

                        if (_chapterRepository != null)
                        {
                            _chapterRepository.Dispose();
                            _chapterRepository = null;
                        }

                        if (_mediaStreamsRepository != null)
                        {
                            _mediaStreamsRepository.Dispose();
                            _mediaStreamsRepository = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error disposing database", ex);
                }
            }
        }

        public IEnumerable<Guid> GetChildren(Guid parentId)
        {
            if (parentId == Guid.Empty)
            {
                throw new ArgumentNullException("parentId");
            }

            CheckDisposed();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "select ItemId from ChildrenIds where ParentId = @ParentId";

                cmd.Parameters.Add(cmd, "@ParentId", DbType.Guid).Value = parentId;

                using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult))
                {
                    while (reader.Read())
                    {
                        yield return reader.GetGuid(0);
                    }
                }
            }
        }

        public IEnumerable<BaseItem> GetChildrenItems(Guid parentId)
        {
            if (parentId == Guid.Empty)
            {
                throw new ArgumentNullException("parentId");
            }

            CheckDisposed();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "select " + string.Join(",", _retriveItemColumns) + " from TypedBaseItems where guid in (select ItemId from ChildrenIds where ParentId = @ParentId)";

                cmd.Parameters.Add(cmd, "@ParentId", DbType.Guid).Value = parentId;

                using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult))
                {
                    while (reader.Read())
                    {
                        var item = GetItem(reader);

                        if (item != null)
                        {
                            yield return item;
                        }
                    }
                }
            }
        }

        public IEnumerable<BaseItem> GetItemsOfType(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException("type");
            }

            CheckDisposed();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "select " + string.Join(",", _retriveItemColumns) + " from TypedBaseItems where type = @type";

                cmd.Parameters.Add(cmd, "@type", DbType.String).Value = type.FullName;

                using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult))
                {
                    while (reader.Read())
                    {
                        var item = GetItem(reader);

                        if (item != null)
                        {
                            yield return item;
                        }
                    }
                }
            }
        }

        public QueryResult<BaseItem> GetItems(InternalItemsQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            CheckDisposed();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "select " + string.Join(",", _retriveItemColumns) + " from TypedBaseItems";

                var whereClauses = GetWhereClauses(query, cmd, false);

                var whereTextWithoutPaging = whereClauses.Count == 0 ?
                    string.Empty :
                    " where " + string.Join(" AND ", whereClauses.ToArray());

                whereClauses = GetWhereClauses(query, cmd, true);

                var whereText = whereClauses.Count == 0 ?
                    string.Empty :
                    " where " + string.Join(" AND ", whereClauses.ToArray());

                cmd.CommandText += whereText;

                cmd.CommandText += GetOrderByText(query);

                if (query.Limit.HasValue)
                {
                    cmd.CommandText += " LIMIT " + query.Limit.Value.ToString(CultureInfo.InvariantCulture);
                }

                cmd.CommandText += "; select count (guid) from TypedBaseItems" + whereTextWithoutPaging;

                _logger.Debug(cmd.CommandText);

                var list = new List<BaseItem>();
                var count = 0;

                using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
                {
                    while (reader.Read())
                    {
                        var item = GetItem(reader);
                        if (item != null)
                        {
                            list.Add(item);
                        }
                    }

                    if (reader.NextResult() && reader.Read())
                    {
                        count = reader.GetInt32(0);
                    }
                }

                return new QueryResult<BaseItem>()
                {
                    Items = list.ToArray(),
                    TotalRecordCount = count
                };
            }
        }

        private string GetOrderByText(InternalItemsQuery query)
        {
            if (query.SortBy == null || query.SortBy.Length == 0)
            {
                return string.Empty;
            }

            var sortOrder = query.SortOrder == SortOrder.Descending ? "DESC" : "ASC";

            return " ORDER BY " + string.Join(",", query.SortBy.Select(i => MapOrderByField(i) + " " + sortOrder).ToArray());
        }

        private string MapOrderByField(string name)
        {
            return name;
        }

        public List<Guid> GetItemIdsList(InternalItemsQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            CheckDisposed();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "select guid from TypedBaseItems";

                var whereClauses = GetWhereClauses(query, cmd, true);

                var whereText = whereClauses.Count == 0 ?
                    string.Empty :
                    " where " + string.Join(" AND ", whereClauses.ToArray());

                cmd.CommandText += whereText;

                cmd.CommandText += GetOrderByText(query);

                if (query.Limit.HasValue)
                {
                    cmd.CommandText += " LIMIT " + query.Limit.Value.ToString(CultureInfo.InvariantCulture);
                }

                var list = new List<Guid>();

                _logger.Debug(cmd.CommandText);

                using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
                {
                    while (reader.Read())
                    {
                        list.Add(reader.GetGuid(0));
                    }
                }

                return list;
            }
        }

        public QueryResult<Guid> GetItemIds(InternalItemsQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            CheckDisposed();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "select guid from TypedBaseItems";

                var whereClauses = GetWhereClauses(query, cmd, false);

                var whereTextWithoutPaging = whereClauses.Count == 0 ?
                    string.Empty :
                    " where " + string.Join(" AND ", whereClauses.ToArray());

                whereClauses = GetWhereClauses(query, cmd, true);

                var whereText = whereClauses.Count == 0 ?
                    string.Empty :
                    " where " + string.Join(" AND ", whereClauses.ToArray());

                cmd.CommandText += whereText;

                cmd.CommandText += GetOrderByText(query);

                if (query.Limit.HasValue)
                {
                    cmd.CommandText += " LIMIT " + query.Limit.Value.ToString(CultureInfo.InvariantCulture);
                }

                cmd.CommandText += "; select count (guid) from TypedBaseItems" + whereTextWithoutPaging;

                var list = new List<Guid>();
                var count = 0;

                _logger.Debug(cmd.CommandText);

                using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
                {
                    while (reader.Read())
                    {
                        list.Add(reader.GetGuid(0));
                    }

                    if (reader.NextResult() && reader.Read())
                    {
                        count = reader.GetInt32(0);
                    }
                }

                return new QueryResult<Guid>()
                {
                    Items = list.ToArray(),
                    TotalRecordCount = count
                };
            }
        }

        private List<string> GetWhereClauses(InternalItemsQuery query, IDbCommand cmd, bool addPaging)
        {
            var whereClauses = new List<string>();

            if (query.IsCurrentSchema.HasValue)
            {
                if (query.IsCurrentSchema.Value)
                {
                    whereClauses.Add("(SchemaVersion not null AND SchemaVersion=@SchemaVersion)");
                }
                else
                {
                    whereClauses.Add("(SchemaVersion is null or SchemaVersion<>@SchemaVersion)");
                }
                cmd.Parameters.Add(cmd, "@SchemaVersion", DbType.Int32).Value = LatestSchemaVersion;
            }
            if (query.IsMovie.HasValue)
            {
                whereClauses.Add("IsMovie=@IsMovie");
                cmd.Parameters.Add(cmd, "@IsMovie", DbType.Boolean).Value = query.IsMovie;
            }
            if (query.IsKids.HasValue)
            {
                whereClauses.Add("IsKids=@IsKids");
                cmd.Parameters.Add(cmd, "@IsKids", DbType.Boolean).Value = query.IsKids;
            }
            if (query.IsSports.HasValue)
            {
                whereClauses.Add("IsSports=@IsSports");
                cmd.Parameters.Add(cmd, "@IsSports", DbType.Boolean).Value = query.IsSports;
            }

            var includeTypes = query.IncludeItemTypes.SelectMany(MapIncludeItemTypes).ToArray();
            if (includeTypes.Length == 1)
            {
                whereClauses.Add("type=@type");
                cmd.Parameters.Add(cmd, "@type", DbType.String).Value = includeTypes[0];
            }
            else if (includeTypes.Length > 1)
            {
                var inClause = string.Join(",", includeTypes.Select(i => "'" + i + "'").ToArray());
                whereClauses.Add(string.Format("type in ({0})", inClause));
            }

            var excludeTypes = query.ExcludeItemTypes.SelectMany(MapIncludeItemTypes).ToArray();
            if (excludeTypes.Length == 1)
            {
                whereClauses.Add("type<>@type");
                cmd.Parameters.Add(cmd, "@type", DbType.String).Value = excludeTypes[0];
            }
            else if (excludeTypes.Length > 1)
            {
                var inClause = string.Join(",", excludeTypes.Select(i => "'" + i + "'").ToArray());
                whereClauses.Add(string.Format("type not in ({0})", inClause));
            }

            if (query.ChannelIds.Length == 1)
            {
                whereClauses.Add("ChannelId=@ChannelId");
                cmd.Parameters.Add(cmd, "@ChannelId", DbType.String).Value = query.ChannelIds[0];
            }
            if (query.ChannelIds.Length > 1)
            {
                var inClause = string.Join(",", query.ChannelIds.Select(i => "'" + i + "'").ToArray());
                whereClauses.Add(string.Format("ChannelId in ({0})", inClause));
            }

            if (query.MinEndDate.HasValue)
            {
                whereClauses.Add("EndDate>=@MinEndDate");
                cmd.Parameters.Add(cmd, "@MinEndDate", DbType.Date).Value = query.MinEndDate.Value;
            }

            if (query.MaxEndDate.HasValue)
            {
                whereClauses.Add("EndDate<=@MaxEndDate");
                cmd.Parameters.Add(cmd, "@MaxEndDate", DbType.Date).Value = query.MaxEndDate.Value;
            }

            if (query.MinStartDate.HasValue)
            {
                whereClauses.Add("StartDate>=@MinStartDate");
                cmd.Parameters.Add(cmd, "@MinStartDate", DbType.Date).Value = query.MinStartDate.Value;
            }

            if (query.MaxStartDate.HasValue)
            {
                whereClauses.Add("StartDate<=@MaxStartDate");
                cmd.Parameters.Add(cmd, "@MaxStartDate", DbType.Date).Value = query.MaxStartDate.Value;
            }

            if (query.IsAiring.HasValue)
            {
                if (query.IsAiring.Value)
                {
                    whereClauses.Add("StartDate<=@MaxStartDate");
                    cmd.Parameters.Add(cmd, "@MaxStartDate", DbType.Date).Value = DateTime.UtcNow;

                    whereClauses.Add("EndDate>=@MinEndDate");
                    cmd.Parameters.Add(cmd, "@MinEndDate", DbType.Date).Value = DateTime.UtcNow;
                }
                else
                {
                    whereClauses.Add("(StartDate>@IsAiringDate OR EndDate < @IsAiringDate)");
                    cmd.Parameters.Add(cmd, "@IsAiringDate", DbType.Date).Value = DateTime.UtcNow;
                }
            }

            if (!string.IsNullOrWhiteSpace(query.Person))
            {
                whereClauses.Add("Guid in (select ItemId from People where Name=@PersonName)");
                cmd.Parameters.Add(cmd, "@PersonName", DbType.String).Value = query.Person;
            }

            if (!string.IsNullOrWhiteSpace(query.NameContains))
            {
                whereClauses.Add("Name like @NameContains");
                cmd.Parameters.Add(cmd, "@NameContains", DbType.String).Value = "%" + query.NameContains + "%";
            }

            if (query.Genres.Length > 0)
            {
                var genres = new List<string>();
                var index = 0;
                foreach (var genre in query.Genres)
                {
                    genres.Add("Genres like @Genres" + index);
                    cmd.Parameters.Add(cmd, "@Genres" + index, DbType.String).Value = "%" + genre + "%";
                    index++;
                }
                var genreCaluse = "(" + string.Join(" OR ", genres.ToArray()) + ")";
                whereClauses.Add(genreCaluse);
            }

            if (query.MaxParentalRating.HasValue)
            {
                whereClauses.Add("(ParentalRatingValue is NULL OR ParentalRatingValue<=@MaxParentalRating)");
                cmd.Parameters.Add(cmd, "@MaxParentalRating", DbType.Int32).Value = query.MaxParentalRating.Value;
            }

            if (query.HasParentalRating.HasValue)
            {
                if (query.HasParentalRating.Value)
                {
                    whereClauses.Add("ParentalRatingValue NOT NULL");
                }
                else
                {
                    whereClauses.Add("ParentalRatingValue IS NULL");
                }
            }

            if (query.HasDeadParentId.HasValue)
            {
                if (query.HasDeadParentId.Value)
                {
                    whereClauses.Add("ParentId NOT NULL AND ParentId NOT IN (select guid from TypedBaseItems)");
                }
            }

            if (addPaging)
            {
                if (query.StartIndex.HasValue && query.StartIndex.Value > 0)
                {
                    var pagingWhereText = whereClauses.Count == 0 ?
                        string.Empty :
                        " where " + string.Join(" AND ", whereClauses.ToArray());

                    var orderBy = GetOrderByText(query);

                    whereClauses.Add(string.Format("guid NOT IN (SELECT guid FROM TypedBaseItems {0}" + orderBy + " LIMIT {1})",
                        pagingWhereText,
                        query.StartIndex.Value.ToString(CultureInfo.InvariantCulture)));
                }
            }

            return whereClauses;
        }

        private static readonly Type[] KnownTypes =
        {
            typeof(LiveTvProgram),
            typeof(LiveTvChannel),
            typeof(LiveTvVideoRecording),
            typeof(LiveTvAudioRecording),
            typeof(Series),
            typeof(LiveTvAudioRecording),
            typeof(LiveTvVideoRecording),
            typeof(Audio),
            typeof(MusicAlbum),
            typeof(MusicArtist),
            typeof(MusicGenre),
            typeof(MusicVideo),
            typeof(Movie),
            typeof(BoxSet),
            typeof(Episode),
            typeof(Season),
            typeof(Series),
            typeof(Book),
            typeof(CollectionFolder),
            typeof(Folder),
            typeof(Game),
            typeof(GameGenre),
            typeof(GameSystem),
            typeof(Genre),
            typeof(Person),
            typeof(Photo),
            typeof(PhotoAlbum),
            typeof(Studio),
            typeof(UserRootFolder),
            typeof(UserView),
            typeof(Video),
            typeof(Year)
        };

        private static Dictionary<string, string[]> GetTypeMapDictionary()
        {
            var dict = new Dictionary<string, string[]>();

            foreach (var t in KnownTypes)
            {
                dict[t.Name] = new[] { t.FullName };
            }

            dict["Recording"] = new[] { typeof(LiveTvAudioRecording).FullName, typeof(LiveTvVideoRecording).FullName };
            dict["Program"] = new[] { typeof(LiveTvProgram).FullName };
            dict["TvChannel"] = new[] { typeof(LiveTvChannel).FullName };

            return dict;
        }

        // Not crazy about having this all the way down here, but at least it's in one place
        readonly Dictionary<string, string[]> _types = GetTypeMapDictionary();

        private IEnumerable<string> MapIncludeItemTypes(string value)
        {
            string[] result;
            if (_types.TryGetValue(value, out result))
            {
                return result;
            }

            return new[] { value };
        }

        public async Task DeleteItem(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentNullException("id");
            }

            CheckDisposed();

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            IDbTransaction transaction = null;

            try
            {
                transaction = _connection.BeginTransaction();

                // First delete children
                _deleteChildrenCommand.GetParameter(0).Value = id;
                _deleteChildrenCommand.Transaction = transaction;
                _deleteChildrenCommand.ExecuteNonQuery();

                // Delete people
                _deletePeopleCommand.GetParameter(0).Value = id;
                _deletePeopleCommand.Transaction = transaction;
                _deletePeopleCommand.ExecuteNonQuery();

                // Delete the item
                _deleteItemCommand.GetParameter(0).Value = id;
                _deleteItemCommand.Transaction = transaction;
                _deleteItemCommand.ExecuteNonQuery();

                transaction.Commit();
            }
            catch (OperationCanceledException)
            {
                if (transaction != null)
                {
                    transaction.Rollback();
                }

                throw;
            }
            catch (Exception e)
            {
                _logger.ErrorException("Failed to save children:", e);

                if (transaction != null)
                {
                    transaction.Rollback();
                }

                throw;
            }
            finally
            {
                if (transaction != null)
                {
                    transaction.Dispose();
                }

                _writeLock.Release();
            }
        }

        public async Task SaveChildren(Guid parentId, IEnumerable<Guid> children, CancellationToken cancellationToken)
        {
            if (parentId == Guid.Empty)
            {
                throw new ArgumentNullException("parentId");
            }

            if (children == null)
            {
                throw new ArgumentNullException("children");
            }

            CheckDisposed();

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            IDbTransaction transaction = null;

            try
            {
                transaction = _connection.BeginTransaction();

                // First delete 
                _deleteChildrenCommand.GetParameter(0).Value = parentId;
                _deleteChildrenCommand.Transaction = transaction;

                _deleteChildrenCommand.ExecuteNonQuery();

                foreach (var id in children)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _saveChildrenCommand.GetParameter(0).Value = parentId;
                    _saveChildrenCommand.GetParameter(1).Value = id;

                    _saveChildrenCommand.Transaction = transaction;

                    _saveChildrenCommand.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch (OperationCanceledException)
            {
                if (transaction != null)
                {
                    transaction.Rollback();
                }

                throw;
            }
            catch (Exception e)
            {
                _logger.ErrorException("Failed to save children:", e);

                if (transaction != null)
                {
                    transaction.Rollback();
                }

                throw;
            }
            finally
            {
                if (transaction != null)
                {
                    transaction.Dispose();
                }

                _writeLock.Release();
            }
        }

        public IEnumerable<MediaStream> GetMediaStreams(MediaStreamQuery query)
        {
            CheckDisposed();
            return _mediaStreamsRepository.GetMediaStreams(query);
        }

        public Task SaveMediaStreams(Guid id, IEnumerable<MediaStream> streams, CancellationToken cancellationToken)
        {
            CheckDisposed();
            return _mediaStreamsRepository.SaveMediaStreams(id, streams, cancellationToken);
        }

        public List<string> GetPeopleNames(InternalPeopleQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            CheckDisposed();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "select Distinct Name from People";

                var whereClauses = GetPeopleWhereClauses(query, cmd);

                if (whereClauses.Count > 0)
                {
                    cmd.CommandText += "  where " + string.Join(" AND ", whereClauses.ToArray());
                }

                cmd.CommandText += " order by ListOrder";

                var list = new List<string>();

                using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult))
                {
                    while (reader.Read())
                    {
                        list.Add(reader.GetString(0));
                    }
                }

                return list;
            }
        }

        public List<PersonInfo> GetPeople(InternalPeopleQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            CheckDisposed();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "select ItemId, Name, Role, PersonType, SortOrder from People";

                var whereClauses = GetPeopleWhereClauses(query, cmd);

                if (whereClauses.Count > 0)
                {
                    cmd.CommandText += "  where " + string.Join(" AND ", whereClauses.ToArray());
                }

                cmd.CommandText += " order by ListOrder";

                var list = new List<PersonInfo>();

                using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult))
                {
                    while (reader.Read())
                    {
                        list.Add(GetPerson(reader));
                    }
                }

                return list;
            }
        }

        private List<string> GetPeopleWhereClauses(InternalPeopleQuery query, IDbCommand cmd)
        {
            var whereClauses = new List<string>();

            if (query.ItemId != Guid.Empty)
            {
                whereClauses.Add("ItemId=@ItemId");
                cmd.Parameters.Add(cmd, "@ItemId", DbType.Guid).Value = query.ItemId;
            }
            if (query.AppearsInItemId != Guid.Empty)
            {
                whereClauses.Add("Name in (Select Name from People where ItemId=@AppearsInItemId)");
                cmd.Parameters.Add(cmd, "@AppearsInItemId", DbType.Guid).Value = query.AppearsInItemId;
            }
            if (query.PersonTypes.Count == 1)
            {
                whereClauses.Add("PersonType=@PersonType");
                cmd.Parameters.Add(cmd, "@PersonType", DbType.String).Value = query.PersonTypes[0];
            }
            if (query.PersonTypes.Count > 1)
            {
                var val = string.Join(",", query.PersonTypes.Select(i => "'" + i + "'").ToArray());

                whereClauses.Add("PersonType in (" + val + ")");
            }
            if (query.ExcludePersonTypes.Count == 1)
            {
                whereClauses.Add("PersonType<>@PersonType");
                cmd.Parameters.Add(cmd, "@PersonType", DbType.String).Value = query.ExcludePersonTypes[0];
            }
            if (query.ExcludePersonTypes.Count > 1)
            {
                var val = string.Join(",", query.ExcludePersonTypes.Select(i => "'" + i + "'").ToArray());

                whereClauses.Add("PersonType not in (" + val + ")");
            }
            if (query.MaxListOrder.HasValue)
            {
                whereClauses.Add("ListOrder<=@MaxListOrder");
                cmd.Parameters.Add(cmd, "@MaxListOrder", DbType.Int32).Value = query.MaxListOrder.Value;
            }
            if (!string.IsNullOrWhiteSpace(query.NameContains))
            {
                whereClauses.Add("Name like @NameContains");
                cmd.Parameters.Add(cmd, "@NameContains", DbType.String).Value = "%" + query.NameContains + "%";
            }

            return whereClauses;
        }

        public async Task UpdatePeople(Guid itemId, List<PersonInfo> people)
        {
            if (itemId == Guid.Empty)
            {
                throw new ArgumentNullException("itemId");
            }

            if (people == null)
            {
                throw new ArgumentNullException("people");
            }

            CheckDisposed();

            var cancellationToken = CancellationToken.None;

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            IDbTransaction transaction = null;

            try
            {
                transaction = _connection.BeginTransaction();

                // First delete 
                _deletePeopleCommand.GetParameter(0).Value = itemId;
                _deletePeopleCommand.Transaction = transaction;

                _deletePeopleCommand.ExecuteNonQuery();

                var listIndex = 0;

                foreach (var person in people)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _savePersonCommand.GetParameter(0).Value = itemId;
                    _savePersonCommand.GetParameter(1).Value = person.Name;
                    _savePersonCommand.GetParameter(2).Value = person.Role;
                    _savePersonCommand.GetParameter(3).Value = person.Type;
                    _savePersonCommand.GetParameter(4).Value = person.SortOrder;
                    _savePersonCommand.GetParameter(5).Value = listIndex;

                    _savePersonCommand.Transaction = transaction;

                    _savePersonCommand.ExecuteNonQuery();
                    listIndex++;
                }

                transaction.Commit();
            }
            catch (OperationCanceledException)
            {
                if (transaction != null)
                {
                    transaction.Rollback();
                }

                throw;
            }
            catch (Exception e)
            {
                _logger.ErrorException("Failed to save people:", e);

                if (transaction != null)
                {
                    transaction.Rollback();
                }

                throw;
            }
            finally
            {
                if (transaction != null)
                {
                    transaction.Dispose();
                }

                _writeLock.Release();
            }
        }

        private PersonInfo GetPerson(IDataReader reader)
        {
            var item = new PersonInfo();

            item.ItemId = reader.GetGuid(0);
            item.Name = reader.GetString(1);

            if (!reader.IsDBNull(2))
            {
                item.Role = reader.GetString(2);
            }

            if (!reader.IsDBNull(3))
            {
                item.Type = reader.GetString(3);
            }

            if (!reader.IsDBNull(4))
            {
                item.SortOrder = reader.GetInt32(4);
            }

            return item;
        }
    }
}