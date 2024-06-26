﻿using System.Collections.Generic;
using System.Linq;
using Shoko.Models.Server;
using Shoko.Server.Databases;

namespace Shoko.Server.Repositories.Direct;

public class IgnoreAnimeRepository : BaseDirectRepository<IgnoreAnime, int>
{
    public IgnoreAnime GetByAnimeUserType(int animeID, int userID, int ignoreType)
    {
        return Lock(() =>
        {
            using var session = _databaseFactory.SessionFactory.OpenSession();
            return session
                .Query<IgnoreAnime>()
                .SingleOrDefault(a => a.AnimeID == animeID && a.JMMUserID == userID && a.IgnoreType == ignoreType);
        });
    }

    public List<IgnoreAnime> GetByUserAndType(int userID, int ignoreType)
    {
        return Lock(() =>
        {
            using var session = _databaseFactory.SessionFactory.OpenSession();
            return session
                .Query<IgnoreAnime>()
                .Where(a => a.JMMUserID == userID && a.IgnoreType == ignoreType)
                .ToList();
        });
    }

    public List<IgnoreAnime> GetByUser(int userID)
    {
        return Lock(() =>
        {
            using var session = _databaseFactory.SessionFactory.OpenSession();
            return session
                .Query<IgnoreAnime>()
                .Where(a => a.JMMUserID == userID)
                .ToList();
        });
    }

    public IgnoreAnimeRepository(DatabaseFactory databaseFactory) : base(databaseFactory)
    {
    }
}
