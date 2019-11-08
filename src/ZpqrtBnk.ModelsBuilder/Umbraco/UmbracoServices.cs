﻿using System;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Core.Composing;
using Umbraco.Core.Models;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Core.Services;
using ZpqrtBnk.ModelsBuilder.Building;
using ZpqrtBnk.ModelsBuilder.Configuration;

namespace ZpqrtBnk.ModelsBuilder.Umbraco
{
    public class UmbracoServices
    {
        private readonly IContentTypeService _contentTypeService;
        private readonly IMediaTypeService _mediaTypeService;
        private readonly IMemberTypeService _memberTypeService;
        private readonly IPublishedContentTypeFactory _publishedContentTypeFactory;

        public UmbracoServices(IContentTypeService contentTypeService, IMediaTypeService mediaTypeService, IMemberTypeService memberTypeService, IPublishedContentTypeFactory publishedContentTypeFactory)
        {
            _contentTypeService = contentTypeService;
            _mediaTypeService = mediaTypeService;
            _memberTypeService = memberTypeService;
            _publishedContentTypeFactory = publishedContentTypeFactory;
        }

        private static Config Config => Current.Configs.ModelsBuilder();

        #region Services

        public IList<ContentTypeModel> GetAllTypes()
        {
            var types = new List<ContentTypeModel>();

            types.AddRange(GetTypes(PublishedItemType.Content, _contentTypeService.GetAll().Cast<IContentTypeComposition>().ToArray()));
            types.AddRange(GetTypes(PublishedItemType.Media, _mediaTypeService.GetAll().Cast<IContentTypeComposition>().ToArray()));
            types.AddRange(GetTypes(PublishedItemType.Member, _memberTypeService.GetAll().Cast<IContentTypeComposition>().ToArray()));

            return EnsureDistinctAliases(types);
        }

        public IList<ContentTypeModel> GetContentTypes()
        {
            var contentTypes = _contentTypeService.GetAll().Cast<IContentTypeComposition>().ToArray();
            return GetTypes(PublishedItemType.Content, contentTypes); // aliases have to be unique here
        }

        public IList<ContentTypeModel> GetMediaTypes()
        {
            var contentTypes = _mediaTypeService.GetAll().Cast<IContentTypeComposition>().ToArray();
            return GetTypes(PublishedItemType.Media, contentTypes); // aliases have to be unique here
        }

        public IList<ContentTypeModel> GetMemberTypes()
        {
            var memberTypes = _memberTypeService.GetAll().Cast<IContentTypeComposition>().ToArray();
            return GetTypes(PublishedItemType.Member, memberTypes); // aliases have to be unique here
        }

        private IList<ContentTypeModel> GetTypes(PublishedItemType itemType, IContentTypeComposition[] contentTypes)
        {
            var typeModels = new List<ContentTypeModel>();

            // get the types and the properties
            foreach (var contentType in contentTypes)
            {
                var typeModel = new ContentTypeModel
                {
                    Id = contentType.Id,
                    Alias = contentType.Alias,
                    ParentId = contentType.ParentId,

                    Name = contentType.Name,
                    Description = contentType.Description,
                    Variations = contentType.Variations
                };

                var publishedContentType = _publishedContentTypeFactory.CreateContentType(contentType);
                switch (itemType)
                {
                    case PublishedItemType.Content:
                        typeModel.ItemType = publishedContentType.ItemType == PublishedItemType.Element
                            ? ContentTypeModel.ItemTypes.Element
                            : ContentTypeModel.ItemTypes.Content;
                        break;
                    case PublishedItemType.Media:
                        typeModel.ItemType = publishedContentType.ItemType == PublishedItemType.Element
                            ? ContentTypeModel.ItemTypes.Element
                            : ContentTypeModel.ItemTypes.Media;
                        break;
                    case PublishedItemType.Member:
                        typeModel.ItemType = publishedContentType.ItemType == PublishedItemType.Element
                            ? ContentTypeModel.ItemTypes.Element
                            : ContentTypeModel.ItemTypes.Member;
                        break;
                    default:
                        throw new InvalidOperationException(string.Format("Unsupported PublishedItemType \"{0}\".", itemType));
                }

                typeModels.Add(typeModel);

                foreach (var propertyType in contentType.PropertyTypes)
                {
                    var propertyModel = new PropertyModel
                    {
                        Alias = propertyType.Alias,

                        Name = propertyType.Name,
                        Description = propertyType.Description,
                        Variations = propertyType.Variations
                    };

                    var publishedPropertyType = publishedContentType.GetPropertyType(propertyType.Alias);
                    if (publishedPropertyType == null)
                        throw new Exception($"Panic: could not get published property type {contentType.Alias}.{propertyType.Alias}.");

                    propertyModel.ModelClrType = publishedPropertyType.ModelClrType;

                    typeModel.Properties.Add(propertyModel);
                }
            }

            // wire the base types
            foreach (var typeModel in typeModels.Where(x => x.ParentId > 0))
            {
                typeModel.BaseType = typeModels.SingleOrDefault(x => x.Id == typeModel.ParentId);
                // Umbraco 7.4 introduces content types containers, so even though ParentId > 0, the parent might
                // not be a content type - here we assume that BaseType being null while ParentId > 0 means that
                // the parent is a container (and we don't check).
                typeModel.IsParent = typeModel.BaseType != null;
            }

            // discover mixins
            foreach (var contentType in contentTypes)
            {
                var typeModel = typeModels.SingleOrDefault(x => x.Id == contentType.Id);
                if (typeModel == null) throw new Exception("Panic: no type model matching content type.");

                IEnumerable<IContentTypeComposition> compositionTypes;
                var contentTypeAsMedia = contentType as IMediaType;
                var contentTypeAsContent = contentType as IContentType;
                var contentTypeAsMember = contentType as IMemberType;
                if (contentTypeAsMedia != null) compositionTypes = contentTypeAsMedia.ContentTypeComposition;
                else if (contentTypeAsContent != null) compositionTypes = contentTypeAsContent.ContentTypeComposition;
                else if (contentTypeAsMember != null) compositionTypes = contentTypeAsMember.ContentTypeComposition;
                else throw new Exception(string.Format("Panic: unsupported type \"{0}\".", contentType.GetType().FullName));

                foreach (var compositionType in compositionTypes)
                {
                    var compositionModel = typeModels.SingleOrDefault(x => x.Id == compositionType.Id);
                    if (compositionModel == null) throw new Exception("Panic: composition type does not exist.");

                    if (compositionType.Id == contentType.ParentId) continue;

                    // add to mixins
                    typeModel.MixinTypes.Add(compositionModel);

                    // mark as mixin - as well as parents
                    compositionModel.IsMixin = true;
                    while ((compositionModel = compositionModel.BaseType) != null)
                        compositionModel.IsMixin = true;
                }
            }

            return typeModels;
        }

        internal static IList<ContentTypeModel> EnsureDistinctAliases(IList<ContentTypeModel> typeModels)
        {
            var groups = typeModels.GroupBy(x => x.Alias.ToLowerInvariant());
            foreach (var group in groups.Where(x => x.Count() > 1))
            {
                throw new NotSupportedException($"Alias \"{group.Key}\" is used by types"
                    + $" {string.Join(", ", group.Select(x => x.ItemType + ":\"" + x.Alias + "\""))}. Aliases have to be unique."
                    + " One of the aliases must be modified in order to use the ModelsBuilder.");
            }
            return typeModels;
        }

        #endregion
    }
}
