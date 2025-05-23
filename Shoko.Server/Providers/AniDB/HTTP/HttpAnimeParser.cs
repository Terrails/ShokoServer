﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Web;
using System.Xml;
using Microsoft.Extensions.Logging;
using Shoko.Models.Enums;
using Shoko.Plugin.Abstractions.DataModels;
using Shoko.Server.Providers.AniDB.HTTP.GetAnime;

namespace Shoko.Server.Providers.AniDB.HTTP;

public class HttpAnimeParser
{
    private static readonly TimeZoneInfo _japanTime = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
    private readonly ILogger<HttpAnimeParser> _logger;

    public HttpAnimeParser(ILogger<HttpAnimeParser> logger)
    {
        _logger = logger;
    }

    public ResponseGetAnime Parse(int animeId, string input)
    {
        var xml = ParseXml(input);
        if (xml == null)
        {
            return null;
        }

        var titles = ParseTitles(xml["anime"]?["titles"]?.GetElementsByTagName("title"));
        var anime = ParseAnime(animeId, titles, xml);
        if (anime == null)
        {
            return null;
        }

        var episodes = ParseEpisodes(animeId, xml);
        var tags = ParseTags(animeId, xml);
        var staff = ParseStaffs(animeId, xml);
        var characters = ParseCharacters(animeId, xml);
        var relations = ParseRelations(animeId, xml);
        var resources = ParseResources(animeId, xml);
        var similar = ParseSimilar(animeId, xml);

        var response = new ResponseGetAnime
        {
            Anime = anime,
            Titles = titles,
            Episodes = episodes,
            Tags = tags,
            Staff = staff,
            Characters = characters,
            Relations = relations,
            Resources = resources,
            Similar = similar
        };
        return response;
    }

    private static XmlDocument ParseXml(string input)
    {
        var docAnime = new XmlDocument();
        docAnime.LoadXml(input);
        return docAnime;
    }

    #region Parse Anime Details

    private ResponseAnime ParseAnime(int animeID, List<ResponseTitle> titles, XmlDocument docAnime)
    {
        // most of the general anime data will be overwritten by the UDP command
        var anime = new ResponseAnime { AnimeID = animeID };

        // check if there is any data
        if (docAnime?["anime"]?.Attributes["id"]?.Value == null)
        {
            _logger.LogWarning("AniDB ProcessAnimeDetails - Received no or invalid info in XML");
            return null;
        }

        anime.Description = TryGetProperty(docAnime, "anime", "description")?.Replace('`', '\'');
        var type = TryGetProperty(docAnime, "anime", "type");
        anime.AnimeType = type.ToLowerInvariant() switch
        {
            "movie" => AnimeType.Movie,
            "ova" => AnimeType.OVA,
            "tv series" => AnimeType.TVSeries,
            "tv special" => AnimeType.TVSpecial,
            "web" => AnimeType.Web,
            "music video" => AnimeType.MusicVideo,
            "other" => AnimeType.Other,
            _ => AnimeType.Unknown,
        };

        var episodeCount = TryGetProperty(docAnime, "anime", "episodecount");
        int.TryParse(episodeCount, out var epCount);
        anime.EpisodeCount = epCount;
        anime.EpisodeCountNormal = epCount;

        ParseDates(docAnime, anime);

        var restricted = docAnime["anime"].Attributes["restricted"]?.Value;
        if (bool.TryParse(restricted, out var res))
        {
            anime.IsRestricted = res;
        }
        else
        {
            anime.IsRestricted = false;
        }

        anime.URL = TryGetProperty(docAnime, "anime", "url");
        anime.Picname = TryGetProperty(docAnime, "anime", "picture");

        anime.MainTitle = titles.FirstOrDefault(t => t.TitleType == TitleType.Main)?.Title;
        if (string.IsNullOrWhiteSpace(anime.MainTitle))
        {
            _logger.LogWarning("AniDB ProcessAnimeDetails - Could not find a main title");
            return null;
        }

        ParseRatings(docAnime, anime);

        return anime;
    }

    private static void ParseDates(XmlNode docAnime, ResponseAnime anime)
    {
        var dateString = TryGetProperty(docAnime, "anime", "startdate");
        anime.AirDate = null;
        if (!string.IsNullOrEmpty(dateString))
        {
            if (DateTime.TryParseExact(
                    dateString, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date
                ) && date != DateTime.UnixEpoch && date != DateTime.MinValue)
            {
                anime.AirDate = date;
            }
            else if (DateTime.TryParseExact(
                         dateString, "yyyy-MM", CultureInfo.InvariantCulture,
                         DateTimeStyles.None, out date
                     ) && date != DateTime.UnixEpoch && date != DateTime.MinValue)
            {
                anime.AirDate = date;
            }
            else if (DateTime.TryParseExact(
                         dateString, "yyyy", CultureInfo.InvariantCulture,
                         DateTimeStyles.None, out date
                     ) && date != DateTime.UnixEpoch && date != DateTime.MinValue)
            {
                anime.AirDate = date;
            }

            if (anime.AirDate != null)
            {
                // define datetimeoffset to make anime.AirDate timezone aware
                var dto = new DateTimeOffset(anime.AirDate.Value, _japanTime.GetUtcOffset(anime.AirDate.Value));
                anime.AirDate = TimeZoneInfo.ConvertTimeFromUtc(dto.UtcDateTime, _japanTime);
            }
        }

        dateString = TryGetProperty(docAnime, "anime", "enddate");
        anime.EndDate = null;
        if (!string.IsNullOrEmpty(dateString))
        {
            if (DateTime.TryParseExact(
                    dateString, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date
                ) && date != DateTime.UnixEpoch && date != DateTime.MinValue)
            {
                anime.EndDate = date;
            }
            else if (DateTime.TryParseExact(
                         dateString, "yyyy-MM", CultureInfo.InvariantCulture,
                         DateTimeStyles.None, out date
                     ) && date != DateTime.UnixEpoch && date != DateTime.MinValue)
            {
                anime.EndDate = date;
            }
            else if (DateTime.TryParseExact(
                         dateString, "yyyy", CultureInfo.InvariantCulture,
                         DateTimeStyles.None, out date
                     ) && date != DateTime.UnixEpoch && date != DateTime.MinValue)
            {
                anime.EndDate = date;
            }
            
            if (anime.EndDate != null)
            {
                // define datetimeoffset to make anime.EndDate timezone aware
                var dto = new DateTimeOffset(anime.EndDate.Value, _japanTime.GetUtcOffset(anime.EndDate.Value));
                anime.EndDate = TimeZoneInfo.ConvertTimeFromUtc(dto.UtcDateTime, _japanTime);
            }

            if (anime.EndDate != null && anime.AirDate != null && anime.EndDate < anime.AirDate) anime.EndDate = anime.AirDate;
        }

        anime.BeginYear = anime.AirDate?.Year ?? 0;
        anime.EndYear = anime.EndDate?.Year ?? 0;
    }

    private static void ParseRatings(XmlNode docAnime, ResponseAnime anime)
    {
        // init ratings
        anime.VoteCount = 0;
        anime.TempVoteCount = 0;
        anime.Rating = 0;
        anime.TempRating = 0;
        anime.ReviewCount = 0;
        anime.AvgReviewRating = 0;

        const NumberStyles style = NumberStyles.Number;
        var culture = CultureInfo.CreateSpecificCulture("en-GB");

        var ratingItems = docAnime["anime"]["ratings"]?.ChildNodes;
        if (ratingItems == null)
        {
            return;
        }

        foreach (XmlNode node in ratingItems)
        {
            var name = node?.Name.Trim().ToLower();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!int.TryParse(TryGetAttribute(node, "count"), out var iCount))
            {
                continue;
            }

            if (!decimal.TryParse(node.InnerText.Trim(), style, culture, out var iRating))
            {
                continue;
            }

            iRating = (int)Math.Round(iRating * 100);

            switch (name)
            {
                case "permanent":
                    anime.VoteCount = iCount;
                    anime.Rating = (int)iRating;
                    break;
                case "temporary":
                    anime.TempVoteCount = iCount;
                    anime.TempRating = (int)iRating;
                    break;
                case "review":
                    anime.ReviewCount = iCount;
                    anime.AvgReviewRating = (int)iRating;
                    break;
            }
        }
    }

    #endregion

    #region Parse Titles

    private List<ResponseTitle> ParseTitles(XmlNodeList titleElements)
    {
        var titles = new List<ResponseTitle>();

        if (titleElements == null)
        {
            return titles;
        }

        foreach (var node in titleElements.OfType<XmlElement>())
        {
            try
            {
                var animeTitle = ParseTitle(node);
                titles.Add(animeTitle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Ex}", ex.ToString());
            }
        }

        return titles;
    }

    private static ResponseTitle ParseTitle(XmlElement node)
    {
        var titleType = TryGetAttribute(node, "type");
        Enum.TryParse(titleType, true, out TitleType type);

        var language = TryGetAttribute(node, "xml:lang");
        var langEnum = GetLanguageFromXmlAttribute(language);
        var title = UnescapeXml(node.InnerText.Trim()).Replace('`', '\'');
        return new ResponseTitle { Title = title, TitleType = type, Language = langEnum };
    }


    private static TitleLanguage GetLanguageFromXmlAttribute(string lang) =>
        lang.ToLowerInvariant() switch
        {
            "ja" => TitleLanguage.Japanese,
            "x-jat" => TitleLanguage.Romaji,
            "en" => TitleLanguage.English,
            "af" => TitleLanguage.Afrikaans,
            "al" => TitleLanguage.Albanian,
            "ar" => TitleLanguage.Arabic,
            "es-pv" => TitleLanguage.Basque,
            "bd" => TitleLanguage.Bengali,
            "bg" => TitleLanguage.Bulgarian,
            "bs" => TitleLanguage.Bosnian,
            "bur" => TitleLanguage.MyanmarBurmese,
            "es-ca" => TitleLanguage.Catalan,
            "x-zht" => TitleLanguage.Pinyin,
            "zh" or "zh-yue" or "zh-cmn" or "zh-nan" => TitleLanguage.Chinese,
            "zh-hant" => TitleLanguage.ChineseTraditional,
            "zh-hans" => TitleLanguage.ChineseSimplified,
            "hr" => TitleLanguage.Croatian,
            "cs" => TitleLanguage.Czech,
            "da" => TitleLanguage.Danish,
            "nl" => TitleLanguage.Dutch,
            "eo" => TitleLanguage.Esperanto,
            "et" => TitleLanguage.Estonian,
            "tl" => TitleLanguage.Filipino,
            "fi" => TitleLanguage.Finnish,
            "fr" => TitleLanguage.French,
            "es-ga" => TitleLanguage.Galician,
            "ka" => TitleLanguage.Georgian,
            "de" => TitleLanguage.German,
            "el" or "grc" => TitleLanguage.Greek,
            "ht" => TitleLanguage.HaitianCreole,
            "he" => TitleLanguage.Hebrew,
            "hi" => TitleLanguage.Hindi,
            "hu" => TitleLanguage.Hungarian,
            "is" => TitleLanguage.Icelandic,
            "id" => TitleLanguage.Indonesian,
            "x-in" => TitleLanguage.Unknown,
            "it" => TitleLanguage.Italian,
            "jv" => TitleLanguage.Javanese,
            "ko" => TitleLanguage.Korean,
            "x-kot" => TitleLanguage.KoreanTranscription,
            "la" => TitleLanguage.Latin,
            "lv" => TitleLanguage.Latvian,
            "lt" => TitleLanguage.Lithuanian,
            "my" => TitleLanguage.Malaysian,
            "mn" => TitleLanguage.Mongolian,
            "ne" => TitleLanguage.Nepali,
            "no" => TitleLanguage.Norwegian,
            "fa" => TitleLanguage.Persian,
            "pl" => TitleLanguage.Polish,
            "pt" => TitleLanguage.Portuguese,
            "pt-br" => TitleLanguage.BrazilianPortuguese,
            "ro" => TitleLanguage.Romanian,
            "ru" => TitleLanguage.Russian,
            "sr" => TitleLanguage.Serbian,
            "si" => TitleLanguage.Sinhala,
            "sk" => TitleLanguage.Slovak,
            "sl" => TitleLanguage.Slovenian,
            "es" or "es-419" => TitleLanguage.Spanish,
            "sv" => TitleLanguage.Swedish,
            "ta" => TitleLanguage.Tamil,
            "tt" => TitleLanguage.Tatar,
            "te" => TitleLanguage.Telugu,
            "th" => TitleLanguage.Thai,
            "x-tht" => TitleLanguage.ThaiTranscription,
            "tr" => TitleLanguage.Turkish,
            "uk" => TitleLanguage.Ukrainian,
            "ur" => TitleLanguage.Urdu,
            "vi" => TitleLanguage.Vietnamese,
            "x-unk" or "x-other" or _ => TitleLanguage.Unknown,
        };

    #endregion

    #region Parse Episodes

    private List<ResponseEpisode> ParseEpisodes(int animeID, XmlNode docAnime)
    {
        var episodes = new List<ResponseEpisode>();
        var episodeItems = docAnime?["anime"]?["episodes"]?.GetElementsByTagName("episode");
        if (episodeItems == null)
        {
            return episodes;
        }

        foreach (XmlElement node in episodeItems)
        {
            try
            {
                var ep = ParseEpisode(animeID, node);

                episodes.Add(ep);
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "{Ex}", exc.ToString());
            }
        }

        return episodes;
    }

    private ResponseEpisode ParseEpisode(int animeID, XmlElement node)
    {
        if (!int.TryParse(node?.Attributes["id"]?.Value, out var id))
        {
            throw new UnexpectedHttpResponseException("Could not get episode ID from XML", HttpStatusCode.OK,
                node?.ToString());
        }
        // default values

        var epNo = TryGetProperty(node, "epno");
        var episodeType = GetEpisodeType(epNo);
        var episodeNumber = GetEpisodeNumber(epNo, episodeType);

        var length = TryGetProperty(node, "length");
        int.TryParse(length, out var lMinutes);
        var secs = lMinutes * 60;

        const NumberStyles style = NumberStyles.Number;
        var culture = CultureInfo.CreateSpecificCulture("en-GB");

        decimal.TryParse(TryGetProperty(node, "rating"), style, culture, out var rating);
        int.TryParse(TryGetAttribute(node, "rating", "votes"), out var votes);
        if (!DateTime.TryParse(TryGetAttribute(node, "update"), out var lastUpdated))
            lastUpdated = DateTime.UnixEpoch;

        var titles = ParseTitles(node.GetElementsByTagName("title"))
            .Where(episodeTitle => !string.IsNullOrEmpty(episodeTitle.Title) && episodeTitle.Language != TitleLanguage.Unknown)
            .ToList();

        var dateString = TryGetProperty(node, "airdate");
        var airDate = GetDate(dateString, true);
        var description = TryGetProperty(node, "summary")?.Replace('`', '\'');

        return new ResponseEpisode
        {
            Description = description,
            EpisodeNumber = episodeNumber,
            EpisodeType = episodeType,
            Rating = rating,
            LengthSeconds = secs,
            Votes = votes,
            EpisodeID = id,
            AnimeID = animeID,
            AirDate = airDate,
            LastUpdated = lastUpdated,
            Titles = titles
        };
    }

    private static int GetEpisodeNumber(string fld, EpisodeType epType)
    {
        // if it is NOT a normal episode strip the leading character
        var fldTemp = fld.Trim();
        if (epType != EpisodeType.Episode)
        {
            fldTemp = fldTemp[1..];
        }

        if (int.TryParse(fldTemp, out var epno))
        {
            return epno;
        }

        // if we couldn't convert to an int, it must mean it is a double episode
        // we will just take the first ep as the episode number
        var sDetails = fldTemp!.Split('-');
        epno = int.Parse(sDetails[0]);
        return epno;
    }

    private static EpisodeType GetEpisodeType(string fld)
    {
        // if the first char is a numeric than it is a normal episode
        if (int.TryParse(fld.Trim()[..1], out _))
        {
            return EpisodeType.Episode;
        }

        // the first character should contain the type of special episode
        // S(special), C(credits), T(trailer), P(parody), O(other)
        // we will just take this and store it in the database
        // this will allow for the user customizing how it is displayed on screen later
        var epType = fld.Trim()[..1].ToUpper();

        return epType switch
        {
            "C" => EpisodeType.Credits,
            "S" => EpisodeType.Special,
            "O" => EpisodeType.Other,
            "T" => EpisodeType.Trailer,
            "P" => EpisodeType.Parody,
            _ => EpisodeType.Episode
        };
    }

    #endregion

    #region Parse Tags

    private List<ResponseTag> ParseTags(int animeID, XmlNode docAnime)
    {
        var tags = new List<ResponseTag>();

        var tagItems = docAnime?["anime"]?["tags"]?.GetElementsByTagName("tag");
        if (tagItems == null)
        {
            return tags;
        }

        foreach (XmlNode node in tagItems)
        {
            try
            {
                var tag = ParseTag(animeID, node);
                if (tag == null)
                {
                    continue;
                }

                tags.Add(tag);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Ex}", ex.ToString());
            }
        }

        return tags;
    }

    private static ResponseTag ParseTag(int animeID, XmlNode node)
    {
        if (!int.TryParse(TryGetAttribute(node, "id"), out var tagID))
        {
            return null;
        }

        var tagName = TryGetProperty(node, "name")?.Replace('`', '\'');
        if (string.IsNullOrEmpty(tagName))
        {
            return null;
        }

        var tagDescription = TryGetProperty(node, "description")?.Replace('`', '\'');
        int.TryParse(TryGetAttribute(node, "parentid"), out var parentTagID);
        int.TryParse(TryGetAttribute(node, "weight"), out var weight);
        bool.TryParse(TryGetAttribute(node, "verified"), out var verified);
        bool.TryParse(TryGetAttribute(node, "localspoiler"), out var lsp);
        bool.TryParse(TryGetAttribute(node, "globalspoiler"), out var gsp);
        if (!DateTime.TryParse(TryGetAttribute(node, "update"), out var lastUpdated))
            lastUpdated = DateTime.UnixEpoch;

        return new ResponseTag
        {
            AnimeID = animeID,
            TagID = tagID,
            ParentTagID = parentTagID > 0 ? parentTagID : null,
            TagName = tagName,
            TagDescription = tagDescription,
            Weight = weight,
            Verified = verified,
            LocalSpoiler = lsp,
            GlobalSpoiler = gsp,
            LastUpdated = lastUpdated,
        };
    }

    #endregion

    #region Parse Staff

    private List<ResponseStaff> ParseStaffs(int animeID, XmlNode docAnime)
    {
        var creators = new List<ResponseStaff>();

        var charItems = docAnime?["anime"]?["creators"]?.GetElementsByTagName("name");
        if (charItems == null)
        {
            return creators;
        }

        foreach (XmlNode node in charItems)
        {
            try
            {
                var staff = ParseStaff(animeID, node);
                creators.Add(staff);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Ex}", ex.ToString());
            }
        }

        return creators;
    }

    private static ResponseStaff ParseStaff(int animeID, XmlNode node)
    {
        if (!int.TryParse(TryGetAttribute(node, "id"), out var creatorID))
        {
            return null;
        }

        var creatorType = TryGetAttribute(node, "type");
        var creatorName = UnescapeXml(node.InnerText).Replace('`', '\'');
        return new ResponseStaff
        {
            AnimeID = animeID, CreatorID = creatorID, CreatorName = creatorName, CreatorType = creatorType
        };
    }

    #endregion

    #region Parse Characters

    private List<ResponseCharacter> ParseCharacters(int animeID, XmlNode docAnime)
    {
        var chars = new List<ResponseCharacter>();

        var charItems = docAnime?["anime"]?["characters"]?.GetElementsByTagName("character");
        if (charItems == null)
        {
            return chars;
        }

        foreach (XmlNode node in charItems)
        {
            try
            {
                var chr = ParseCharacter(animeID, node);
                chars.Add(chr);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Ex}", ex.ToString());
            }
        }

        return chars;
    }

    private static ResponseCharacter ParseCharacter(int animeID, XmlNode node)
    {
        if (!int.TryParse(TryGetAttribute(node, "id"), out var charID))
        {
            return null;
        }

        var characterType = TryGetProperty(node, "charactertype") ?? "Character";
        var characterAppearanceType = TryGetAttribute(node, "type");
        var charName = TryGetProperty(node, "name")?.Replace('`', '\'');
        var charGender = TryGetProperty(node, "gender")?.Replace('`', '\'');
        var charDescription = TryGetProperty(node, "description")?.Replace('`', '\'');
        var picName = TryGetProperty(node, "picture");
        if (!DateTime.TryParse(TryGetAttribute(node, "update"), out var lastUpdated))
            lastUpdated = DateTime.UnixEpoch;

        // parse seiyuus
        var seiyuus = new List<ResponseSeiyuu>();
        foreach (XmlNode nodeChild in node.ChildNodes)
        {
            if (nodeChild?.Name != "seiyuu")
            {
                continue;
            }

            if (!int.TryParse(nodeChild.Attributes?["id"]?.Value, out var seiyuuID))
            {
                continue;
            }

            var seiyuuPic = nodeChild.Attributes["picture"]?.Value ?? string.Empty;
            var seiyuuName = UnescapeXml(nodeChild.InnerText).Replace('`', '\'');
            seiyuus.Add(new ResponseSeiyuu { SeiyuuID = seiyuuID, SeiyuuName = seiyuuName, PicName = seiyuuPic });
        }

        return new ResponseCharacter
        {
            AnimeID = animeID,
            CharacterID = charID,
            CharacterAppearanceType = characterAppearanceType,
            CharacterType = characterType,
            CharacterName = charName,
            CharacterDescription = charDescription,
            PicName = picName,
            Gender = charGender,
            Seiyuus = seiyuus,
            LastUpdated = lastUpdated,
        };
    }

    #endregion

    #region Parse Resources

    private List<ResponseResource> ParseResources(int animeID, XmlNode docAnime)
    {
        var result = new List<ResponseResource>();
        var items = docAnime?["anime"]?["resources"]?.GetElementsByTagName("resource");
        if (items == null)
        {
            return result;
        }

        foreach (XmlNode node in items)
        {
            try
            {
                foreach (XmlNode child in node.ChildNodes)
                {
                    var resourceID = UnescapeXml(child["identifier"]?.InnerText) ??
                                     UnescapeXml(child["url"]?.InnerText);
                    if (!int.TryParse(TryGetAttribute(node, "type"), out var typeInt))
                    {
                        continue;
                    }

                    var resource = new ResponseResource
                    {
                        AnimeID = animeID, ResourceID = resourceID, ResourceType = (AniDB_ResourceLinkType)typeInt
                    };
                    result.Add(resource);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Ex}", ex.ToString());
            }
        }

        return result;
    }

    #endregion

    #region Parse Relations

    private List<ResponseRelation> ParseRelations(int animeID, XmlNode docAnime)
    {
        var rels = new List<ResponseRelation>();

        var relItems = docAnime?["anime"]?["relatedanime"]?.GetElementsByTagName("anime");
        if (relItems == null)
        {
            return rels;
        }

        foreach (XmlNode node in relItems)
        {
            try
            {
                if (!int.TryParse(TryGetAttribute(node, "id"), out var id))
                {
                    continue;
                }

                var type = TryGetAttribute(node, "type");
                var relationType = type.ToLowerInvariant() switch
                {
                    "prequel" => RelationType.Prequel,
                    "sequel" => RelationType.Sequel,
                    "parent story" => RelationType.MainStory,
                    "side story" => RelationType.SideStory,
                    "full story" => RelationType.FullStory,
                    "summary" => RelationType.Summary,
                    "other" => RelationType.Other,
                    "alternative setting" => RelationType.AlternativeSetting,
                    "alternative version" => RelationType.AlternativeVersion,
                    "same setting" => RelationType.SameSetting,
                    "character" => RelationType.SharedCharacters,
                    _ => RelationType.Other
                };
                var relation =
                    new ResponseRelation { AnimeID = animeID, RelationType = relationType, RelatedAnimeID = id };
                rels.Add(relation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Ex}", ex.ToString());
            }
        }

        return rels;
    }

    #endregion

    #region Parse Similar

    private List<ResponseSimilar> ParseSimilar(int animeID, XmlNode docAnime)
    {
        var rels = new List<ResponseSimilar>();

        var simItems = docAnime["anime"]?["similaranime"]?.GetElementsByTagName("anime");
        if (simItems == null)
        {
            return rels;
        }

        foreach (XmlNode node in simItems)
        {
            try
            {
                if (!int.TryParse(TryGetAttribute(node, "id"), out var id))
                {
                    continue;
                }

                int.TryParse(TryGetAttribute(node, "approval"), out var appr);

                int.TryParse(TryGetAttribute(node, "total"), out var tot);
                var sim = new ResponseSimilar { AnimeID = animeID, SimilarAnimeID = id, Approval = appr, Total = tot };
                rels.Add(sim);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Ex}", ex.ToString());
            }
        }

        return rels;
    }

    #endregion

    #region XML Utils

    private static string TryGetProperty(XmlNode doc, string keyName, string propertyName)
    {
        if (doc == null || string.IsNullOrEmpty(keyName) || string.IsNullOrEmpty(propertyName))
        {
            return string.Empty;
        }

        return UnescapeXml(doc[keyName]?[propertyName]?.InnerText.Trim()) ?? string.Empty;
    }

    private static string TryGetProperty(XmlNode node, string propertyName)
    {
        if (node == null || string.IsNullOrEmpty(propertyName))
        {
            return string.Empty;
        }

        return UnescapeXml(node[propertyName]?.InnerText.Trim()) ?? string.Empty;
    }

    private static string TryGetAttribute(XmlNode parentnode, string nodeName, string attName)
    {
        if (parentnode == null || string.IsNullOrEmpty(nodeName) || string.IsNullOrEmpty(attName))
        {
            return string.Empty;
        }

        return parentnode[nodeName]?.Attributes[attName]?.Value ?? string.Empty;
    }

    private static string TryGetAttribute(XmlNode node, string attName)
    {
        if (node == null || string.IsNullOrEmpty(attName))
        {
            return string.Empty;
        }

        return node.Attributes?[attName]?.Value ?? string.Empty;
    }

    private static DateTime? GetDate(string dateXml, bool isStartDate)
    {
        // eg "2008-12-31" or "2008-12" or "2008"
        if (dateXml == null || dateXml.Trim().Length < 4)
        {
            return DateTime.UnixEpoch;
        }

        var year = int.Parse(dateXml.Trim()[..4]);
        var month = dateXml.Trim().Length > 4 ? int.Parse(dateXml.Trim().Substring(5, 2)) : isStartDate ? 1 : 12;
        var day = dateXml.Trim().Length > 7 ? int.Parse(dateXml.Trim().Substring(8, 2)) :
            isStartDate ? 1 : DateTime.DaysInMonth(year, month);

        return new DateTime(year, month, day, 0, 0, 0);
    }

    private static string UnescapeXml(string xml)
    {
        if (xml == null)
        {
            return null;
        }

        string result = null;
        // 5 as a maximum depth is arbitrary, but if we have data that is escaped 5 levels deep, then there's a serious issue.
        for (var i = 0; i < 5; i++)
        {
            var temp = HttpUtility.HtmlDecode(xml);
            if (temp.Equals(result))
            {
                return result;
            }

            result = temp;
        }

        return result;
    }

    #endregion
}
