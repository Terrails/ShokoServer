using System;
using System.Collections.Generic;
using System.Linq;
using Shoko.Models.Server;
using Shoko.Plugin.Abstractions.DataModels;
using Shoko.Plugin.Abstractions.DataModels.Shoko;
using Shoko.Plugin.Abstractions.Enums;
using Shoko.Server.Extensions;
using Shoko.Server.Models.AniDB;
using Shoko.Server.Models.CrossReference;
using Shoko.Server.Models.TMDB;
using Shoko.Server.Repositories;
using Shoko.Server.Utilities;

using EpisodeTypeEnum = Shoko.Models.Enums.EpisodeType;

#nullable enable
namespace Shoko.Server.Models;

public class SVR_AniDB_Episode : AniDB_Episode, IEpisode
{
    public EpisodeType AbstractEpisodeType => (EpisodeType)EpisodeType;

    public EpisodeTypeEnum EpisodeTypeEnum => (EpisodeTypeEnum)EpisodeType;

    public TimeSpan Runtime => TimeSpan.FromSeconds(LengthSeconds);

    public SVR_AniDB_Episode_Title DefaultTitle =>
        RepoFactory.AniDB_Episode_Title.GetByEpisodeIDAndLanguage(EpisodeID, TitleLanguage.English)
            .FirstOrDefault() ?? new() { AniDB_Episode_TitleID = 0, AniDB_EpisodeID = EpisodeID, Language = TitleLanguage.Unknown, Title = $"Episode {EpisodeNumber}" };

    public SVR_AniDB_Episode_Title PreferredTitle => GetPreferredTitle(true)!;

    public SVR_AniDB_Episode_Title? GetPreferredTitle(bool useFallback)
    {
        // Try finding one of the preferred languages.
        foreach (var language in Languages.PreferredEpisodeNamingLanguages)
        {
            if (language.Language == TitleLanguage.Main)
                return DefaultTitle;

            var title = RepoFactory.AniDB_Episode_Title.GetByEpisodeIDAndLanguage(EpisodeID, language.Language)
                .FirstOrDefault();
            if (title is not null)
                return title;
        }

        // Fallback to English if available.
        return useFallback ? DefaultTitle : null;
    }

    public bool HasAired
    {
        get
        {
            if (AniDB_Anime is not { } anidbAnime) return false;
            if (anidbAnime.GetFinishedAiring()) return true;
            var date = this.GetAirDateAsDate();
            if (date == null) return false;

            return date.Value.ToLocalTime() < DateTime.Now;
        }
    }

    public IReadOnlyList<SVR_AniDB_Episode_Title> GetTitles(TitleLanguage? language = null) => language.HasValue
        ? RepoFactory.AniDB_Episode_Title.GetByEpisodeIDAndLanguage(EpisodeID, language.Value)
        : RepoFactory.AniDB_Episode_Title.GetByEpisodeID(EpisodeID);

    #region Images

    public IImageMetadata? GetPreferredImageForType(ImageEntityType entityType)
        => RepoFactory.AniDB_Episode_PreferredImage.GetByAnidbEpisodeIDAndType(EpisodeID, entityType)?.GetImageMetadata();

    public IReadOnlyList<IImageMetadata> GetImages(ImageEntityType? entityType = null)
    {
        var preferredImages = (entityType.HasValue ? [RepoFactory.AniDB_Episode_PreferredImage.GetByAnidbEpisodeIDAndType(EpisodeID, entityType.Value)!] : RepoFactory.AniDB_Episode_PreferredImage.GetByEpisodeID(EpisodeID))
            .WhereNotNull()
            .Select(preferredImage => preferredImage.GetImageMetadata())
            .WhereNotNull()
            .ToDictionary(image => image.ImageType);
        var images = new List<IImageMetadata>();
        if (!entityType.HasValue || entityType.Value is ImageEntityType.Poster)
        {
            var poster = AniDB_Anime?.GetImageMetadata(false);
            if (poster is not null)
                images.Add(preferredImages.TryGetValue(ImageEntityType.Poster, out var preferredPoster) && poster.Equals(preferredPoster)
                    ? preferredPoster
                    : poster
                );
        }
        foreach (var tmdbEpisode in TmdbEpisodes)
            images.AddRange(tmdbEpisode.GetImages(entityType, preferredImages));
        foreach (var tmdbMovie in TmdbMovies)
            images.AddRange(tmdbMovie.GetImages(entityType, preferredImages));

        return images
            .DistinctBy(image => (image.ImageType, image.Source, image.ID))
            .ToList();
    }

    #endregion

    #region Shoko

    public SVR_AnimeSeries? AnimeSeries => RepoFactory.AnimeSeries.GetByAnimeID(AnimeID);

    public SVR_AnimeEpisode? AnimeEpisode => RepoFactory.AnimeEpisode.GetByAniDBEpisodeID(EpisodeID);

    #endregion

    #region AniDB

    public SVR_AniDB_Anime? AniDB_Anime => RepoFactory.AniDB_Anime.GetByAnimeID(AnimeID);

    #endregion

    #region TMDB

    public IReadOnlyList<CrossRef_AniDB_TMDB_Movie> TmdbMovieCrossReferences =>
        RepoFactory.CrossRef_AniDB_TMDB_Movie.GetByAnidbEpisodeID(EpisodeID);

    public IReadOnlyList<TMDB_Movie> TmdbMovies =>
        TmdbMovieCrossReferences
            .Select(xref => xref.TmdbMovie)
            .WhereNotNull()
            .ToList();

    public IReadOnlyList<CrossRef_AniDB_TMDB_Episode> TmdbEpisodeCrossReferences =>
        RepoFactory.CrossRef_AniDB_TMDB_Episode.GetByAnidbEpisodeID(EpisodeID);

    public IReadOnlyList<TMDB_Episode> TmdbEpisodes =>
        TmdbEpisodeCrossReferences
            .Select(xref => xref.TmdbEpisode)
            .WhereNotNull()
            .ToList();

    #endregion

    #region IMetadata Implementation

    DataSourceEnum IMetadata.Source => DataSourceEnum.AniDB;

    int IMetadata<int>.ID => EpisodeID;

    #endregion

    #region IWithTitles Implementation

    string IWithTitles.DefaultTitle => DefaultTitle.Title;

    string IWithTitles.PreferredTitle => PreferredTitle.Title;

    IReadOnlyList<AnimeTitle> IWithTitles.Titles
    {
        get
        {
            var defaultTitle = DefaultTitle;
            return GetTitles()
                .Select(a => new AnimeTitle
                {
                    Source = DataSourceEnum.AniDB,
                    LanguageCode = a.LanguageCode,
                    Language = a.Language,
                    Title = a.Title,
                    Type = string.Equals(a.Title, defaultTitle.Title) ? TitleType.Main : TitleType.None,
                })
                .ToList();
        }
    }

    #endregion

    #region IWithDescription Implementation

    string IWithDescriptions.DefaultDescription => Description;

    string IWithDescriptions.PreferredDescription => Description;

    IReadOnlyList<TextDescription> IWithDescriptions.Descriptions => [
        new()
        {
            Source = DataSourceEnum.AniDB,
            Language = TitleLanguage.English,
            LanguageCode = "en",
            Value = Description ?? string.Empty,
        },
    ];

    #endregion

    #region IWithCastAndCrew Implementation

    IReadOnlyList<ICast> IWithCastAndCrew.Cast => RepoFactory.AniDB_Anime_Character.GetByAnimeID(AnimeID)
        .SelectMany(xref =>
        {
            // We don't want organizations or borked cross-references to show up.
            var character = xref.Character;
            if (character is not { Type: not Server.CharacterType.Organization })
                return [];

            // If a role don't have a creator then we still want it to show up.
            var creatorXrefs = xref.CreatorCrossReferences;
            if (creatorXrefs is { Count: 0 })
                return [new AniDB_Cast(xref, character, null, () => this)];

            return creatorXrefs
                .Select(x => new AniDB_Cast(xref, character, x.CreatorID, () => this));
        })
        .ToList();

    IReadOnlyList<ICrew> IWithCastAndCrew.Crew => RepoFactory.AniDB_Anime_Staff.GetByAnimeID(AnimeID)
        .Select(xref =>
        {
            // Hide studio and actor roles from crew members. Actors should show up as cast, and studios as studios.
            if (xref is { RoleType: Server.CreatorRoleType.Studio or Server.CreatorRoleType.Actor })
                return null;

            return new AniDB_Crew(xref, () => this);
        })
        .WhereNotNull()
        .ToList();

    #endregion

    #region IEpisode Implementation

    int IEpisode.SeriesID => AnimeID;

    EpisodeType IEpisode.Type => (EpisodeType)EpisodeType;

    int? IEpisode.SeasonNumber => EpisodeType == (int)EpisodeTypeEnum.Episode ? 1 : null;

    DateTime? IEpisode.AirDate => this.GetAirDateAsDate();

    ISeries? IEpisode.Series => AniDB_Anime;

    IReadOnlyList<IShokoEpisode> IEpisode.ShokoEpisodes => AnimeEpisode is IShokoEpisode shokoEpisode ? [shokoEpisode] : [];

    IReadOnlyList<IVideoCrossReference> IEpisode.CrossReferences =>
        RepoFactory.CrossRef_File_Episode.GetByEpisodeID(EpisodeID);

    IReadOnlyList<IVideo> IEpisode.VideoList =>
        RepoFactory.CrossRef_File_Episode.GetByEpisodeID(EpisodeID)
            .DistinctBy(xref => xref.Hash)
            .Select(xref => xref.VideoLocal)
            .WhereNotNull()
            .ToList();

    IReadOnlyList<int> IEpisode.ShokoEpisodeIDs => RepoFactory.AnimeEpisode.GetByAniDBEpisodeID(EpisodeID) is { } episode ? [episode.AnimeEpisodeID] : [];

    #endregion
}
