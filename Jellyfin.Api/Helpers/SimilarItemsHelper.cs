﻿using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Api.Helpers
{
    /// <summary>
    /// The similar items helper class.
    /// </summary>
    public static class SimilarItemsHelper
    {
        internal static QueryResult<BaseItemDto> GetSimilarItemsResult(
            DtoOptions dtoOptions,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            Guid? userId,
            string id,
            string? excludeArtistIds,
            int? limit,
            Type[] includeTypes,
            Func<BaseItem, List<PersonInfo>, List<PersonInfo>, BaseItem, int> getSimilarityScore)
        {
            var user = userId.HasValue && !userId.Equals(Guid.Empty)
                ? userManager.GetUserById(userId.Value)
                : null;

            var item = string.IsNullOrEmpty(id) ?
                (!userId.Equals(Guid.Empty) ? libraryManager.GetUserRootFolder() :
                libraryManager.RootFolder) : libraryManager.GetItemById(id);

            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = includeTypes.Select(i => i.Name).ToArray(),
                Recursive = true,
                DtoOptions = dtoOptions,
                ExcludeArtistIds = RequestHelpers.GetGuids(excludeArtistIds)
            };

            var inputItems = libraryManager.GetItemList(query);

            var items = GetSimilaritems(item, libraryManager, inputItems, getSimilarityScore)
                .ToList();

            var returnItems = items;

            if (limit.HasValue && limit < returnItems.Count)
            {
                returnItems = returnItems.GetRange(0, limit.Value);
            }

            var dtos = dtoService.GetBaseItemDtos(returnItems, dtoOptions, user);

            return new QueryResult<BaseItemDto>
            {
                Items = dtos,
                TotalRecordCount = items.Count
            };
        }

        /// <summary>
        /// Gets the similaritems.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="inputItems">The input items.</param>
        /// <param name="getSimilarityScore">The get similarity score.</param>
        /// <returns>IEnumerable{BaseItem}.</returns>
        private static IEnumerable<BaseItem> GetSimilaritems(
            BaseItem item,
            ILibraryManager libraryManager,
            IEnumerable<BaseItem> inputItems,
            Func<BaseItem, List<PersonInfo>, List<PersonInfo>, BaseItem, int> getSimilarityScore)
        {
            var itemId = item.Id;
            inputItems = inputItems.Where(i => i.Id != itemId);
            var itemPeople = libraryManager.GetPeople(item);
            var allPeople = libraryManager.GetPeople(new InternalPeopleQuery
            {
                AppearsInItemId = item.Id
            });

            return inputItems.Select(i => new Tuple<BaseItem, int>(i, getSimilarityScore(item, itemPeople, allPeople, i)))
                .Where(i => i.Item2 > 2)
                .OrderByDescending(i => i.Item2)
                .Select(i => i.Item1);
        }

        private static IEnumerable<string> GetTags(BaseItem item)
        {
            return item.Tags;
        }

        /// <summary>
        /// Gets the similiarity score.
        /// </summary>
        /// <param name="item1">The item1.</param>
        /// <param name="item1People">The item1 people.</param>
        /// <param name="allPeople">All people.</param>
        /// <param name="item2">The item2.</param>
        /// <returns>System.Int32.</returns>
        internal static int GetSimiliarityScore(BaseItem item1, List<PersonInfo> item1People, List<PersonInfo> allPeople, BaseItem item2)
        {
            var points = 0;

            if (!string.IsNullOrEmpty(item1.OfficialRating) && string.Equals(item1.OfficialRating, item2.OfficialRating, StringComparison.OrdinalIgnoreCase))
            {
                points += 10;
            }

            // Find common genres
            points += item1.Genres.Where(i => item2.Genres.Contains(i, StringComparer.OrdinalIgnoreCase)).Sum(i => 10);

            // Find common tags
            points += GetTags(item1).Where(i => GetTags(item2).Contains(i, StringComparer.OrdinalIgnoreCase)).Sum(i => 10);

            // Find common studios
            points += item1.Studios.Where(i => item2.Studios.Contains(i, StringComparer.OrdinalIgnoreCase)).Sum(i => 3);

            var item2PeopleNames = allPeople.Where(i => i.ItemId == item2.Id)
                .Select(i => i.Name)
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .DistinctNames()
                .ToDictionary(i => i, StringComparer.OrdinalIgnoreCase);

            points += item1People.Where(i => item2PeopleNames.ContainsKey(i.Name)).Sum(i =>
            {
                if (string.Equals(i.Type, PersonType.Director, StringComparison.OrdinalIgnoreCase) || string.Equals(i.Role, PersonType.Director, StringComparison.OrdinalIgnoreCase))
                {
                    return 5;
                }

                if (string.Equals(i.Type, PersonType.Actor, StringComparison.OrdinalIgnoreCase) || string.Equals(i.Role, PersonType.Actor, StringComparison.OrdinalIgnoreCase))
                {
                    return 3;
                }

                if (string.Equals(i.Type, PersonType.Composer, StringComparison.OrdinalIgnoreCase) || string.Equals(i.Role, PersonType.Composer, StringComparison.OrdinalIgnoreCase))
                {
                    return 3;
                }

                if (string.Equals(i.Type, PersonType.GuestStar, StringComparison.OrdinalIgnoreCase) || string.Equals(i.Role, PersonType.GuestStar, StringComparison.OrdinalIgnoreCase))
                {
                    return 3;
                }

                if (string.Equals(i.Type, PersonType.Writer, StringComparison.OrdinalIgnoreCase) || string.Equals(i.Role, PersonType.Writer, StringComparison.OrdinalIgnoreCase))
                {
                    return 2;
                }

                return 1;
            });

            if (item1.ProductionYear.HasValue && item2.ProductionYear.HasValue)
            {
                var diff = Math.Abs(item1.ProductionYear.Value - item2.ProductionYear.Value);

                // Add if they came out within the same decade
                if (diff < 10)
                {
                    points += 2;
                }

                // And more if within five years
                if (diff < 5)
                {
                    points += 2;
                }
            }

            return points;
        }
    }
}
