﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using System.Xml.Serialization;
using MongoDB.Bson;
using MongoDB.Driver;

namespace PDS.Witsml.Server.Data
{
    /// <summary>
    /// Encloses MongoDb query method and its helper methods
    /// </summary>
    /// <typeparam name="TList">The type of the parent object for the list of queried data object.</typeparam>
    /// <typeparam name="T">The type of queried data object.</typeparam>
    public class MongoDbQuery<TList, T>
    {
        private IMongoCollection<T> _collection;
        private WitsmlQueryParser _parser;
        private List<string> _fields;
        private string _idPropertyName;

        /// <summary>
        /// Initializes a new instance of the <see cref="MongoDbQuery{TList, T}"/> class.
        /// </summary>
        /// <param name="collection">The name of database collection.</param>
        /// <param name="parser">The parser.</param>
        /// <param name="fields">The fields of the data object to be selected.</param>
        /// <param name="idPropertyName">Name of the identifier property.</param>
        public MongoDbQuery(IMongoCollection<T> collection, WitsmlQueryParser parser, List<string> fields, string idPropertyName)
        {
            _collection = collection;
            _parser = parser;
            _fields = fields;
            _idPropertyName = idPropertyName;
        }

        /// <summary>
        /// Executes this MongoDb query.
        /// </summary>
        /// <returns>The list of queried data object.</returns>
        public List<T> Execute()
        {
            var list = _parser.Parse<TList>(_parser.Context.Xml);
            var info = typeof(TList).GetProperty(typeof(T).Name);
            var tList = info.GetValue(list) as List<T>;

            var entities = new List<T>();
            var returnElements = _parser.ReturnElements();

            foreach (var entity in tList)
            {
                // Build Mongo filter
                var filter = BuildFilter(_parser, entity);
                var results = _collection.Find(filter ?? "{}");

                // Format response using MongoDb projection, i.e. selecting specified fields only
                if (returnElements == OptionsIn.ReturnElements.All.Value)
                    entities.AddRange(results.ToList());
                else if (returnElements == OptionsIn.ReturnElements.IdOnly.Value || returnElements == OptionsIn.ReturnElements.Requested.Value)
                {
                    var projection = BuildProjection(_parser, entity, _fields ?? new List<string>());
                    if (projection != null)
                        results = results.Project<T>(projection);
                    entities.AddRange(results.ToList());
                }
            }

            return entities;
        }

        /// <summary>
        /// Builds the query filter.
        /// </summary>
        /// <param name="parser">The parser.</param>
        /// <param name="entity">The entity to be queried.</param>
        /// <returns>The filter object that for the selection criteria for the queried entity.</returns>
        private FilterDefinition<T> BuildFilter(WitsmlQueryParser parser, T entity)
        {
            var properties = GetPropertyInfo(entity.GetType());
            var filters = new List<FilterDefinition<T>>();

            foreach (var property in properties)
            {
                var filter = BuildFilterForAProperty(property, entity);
                if (filter != null)
                    filters.Add(filter);
            }

            if (filters.Count > 0)
                return Builders<T>.Filter.And(filters);
            else
                return null;
        }

        /// <summary>
        /// Builds the query filter for a property recursively.
        /// </summary>
        /// <param name="propertyInfo">The property information.</param>
        /// <param name="obj">The object that contains the property.</param>
        /// <param name="path">The path of the property to the entity to be queried in the data store.</param>
        /// <returns>The filter object that for the selection criteria for a property.</returns>
        private FilterDefinition<T> BuildFilterForAProperty(PropertyInfo propertyInfo, object obj, string path = null)
        {
            var propertyValue = propertyInfo.GetValue(obj);
            if (propertyValue == null)
                return null;

            var fieldName = propertyInfo.Name;
            if (!string.IsNullOrEmpty(path))
                fieldName = string.Format("{0}.{1}", path, fieldName);
            var properties = GetPropertyInfo(propertyValue.GetType()).ToList();
            var filters = new List<FilterDefinition<T>>();

            if (properties.Count > 0)
            {
                foreach (var property in properties)
                {
                    var filter = BuildFilterForAProperty(property, propertyValue, fieldName);
                    if (filter != null)
                        filters.Add(filter);
                }
            }
            else
            {
                var propertyType = propertyInfo.PropertyType;

                if (propertyType == typeof(string))
                {
                    var strValue = propertyValue.ToString();
                    if (string.IsNullOrEmpty(strValue))
                        return null;

                    return Builders<T>.Filter.Regex(fieldName, new BsonRegularExpression("/^" + strValue + "$/i"));
                }
                else if (propertyValue is IEnumerable)
                {
                    var listFilters = new List<FilterDefinition<T>>();
                    var list = (IEnumerable)propertyValue;
                    foreach (var item in list)
                    {
                        var itemFilters = new List<FilterDefinition<T>>();
                        var itemProperties = GetPropertyInfo(item.GetType());
                        foreach (var itemProperty in itemProperties)
                        {
                            var itemFilter = BuildFilterForAProperty(itemProperty, item, fieldName);
                            if (itemFilter != null)
                                itemFilters.Add(itemFilter);
                        }
                        if (itemFilters.Count > 0)
                            listFilters.Add(Builders<T>.Filter.And(itemFilters));
                    }
                    if (listFilters.Count > 0)
                        filters.Add(Builders<T>.Filter.Or(listFilters));
                }
                else
                {
                    if (propertyInfo.Name == "DateTimeCreation" || propertyInfo.Name == "DateTimeLastChange")
                        return Builders<T>.Filter.Gte(fieldName, propertyValue);
                    else
                        return Builders<T>.Filter.Eq(fieldName, propertyValue);
                }
            }

            if (filters.Count > 0)
                return Builders<T>.Filter.And(filters);
            else
                return null;
        }

        private IEnumerable<PropertyInfo> GetPropertyInfo(Type t)
        {
            return t.GetProperties().Where(p => p.IsDefined(typeof(XmlElementAttribute), false) || p.IsDefined(typeof(XmlAttributeAttribute), false));
        }

        /// <summary>
        /// Builds the projection for the query.
        /// </summary>
        /// <param name="parser">The parser.</param>
        /// <returns>The projection object that contains the fields to be selected.</returns>
        private ProjectionDefinition<T> BuildProjection(WitsmlQueryParser parser, T entity, List<string> fields)
        {
            var element = parser.Element();
            if (element == null)
                return null;

            var properties = GetPropertyInfo(typeof(T));

            foreach (var child in element.Elements())
            {
                var property = GetPropertyInfoForAnElement(properties, child);
                var value = property.GetValue(entity);
                BuildProjectionForAnElement(child, null, fields, property, value);
            }

            if (fields.Count == 0)
                return Builders<T>.Projection.Exclude(_idPropertyName).Include(string.Empty);
            else {
                var projection = Builders<T>.Projection.Include(fields[0]);
                for (var i = 1; i < fields.Count; i++)
                    projection = projection.Include(fields[i]);
                if (!fields.Contains(_idPropertyName))
                    projection = projection.Exclude(_idPropertyName);
                return projection;
            }
        }

        /// <summary>
        /// Builds the projection for an element in the QueryIn XML recursively.
        /// </summary>
        /// <param name="element">The element.</param>
        /// <param name="fieldPath">The field path to the top level property of the queried entity.</param>
        /// <param name="fields">The list of fields to be selecte for the element.</param>
        private void BuildProjectionForAnElement(XElement element, string fieldPath, List<string> fields, PropertyInfo propertyInfo, object propertyValue)
        {
            var prefix = string.IsNullOrEmpty(fieldPath) ? string.Empty : string.Format("{0}.", fieldPath);
            var path = string.Format("{0}{1}", prefix, CaptalizeString(propertyInfo.Name));
            if (!element.HasElements && !element.HasAttributes)
            {
                if (!fields.Contains(path))
                    fields.Add(path);
                return;
            }

            var properties = GetPropertyInfo(propertyValue.GetType());

            if (element.HasElements)
            {
                foreach (var child in element.Elements())
                {
                    var property = GetPropertyInfoForAnElement(properties, child);
                    var value = property.GetValue(propertyValue);
                    BuildProjectionForAnElement(child, path, fields, property, value);
                }
            }
            if (element.HasAttributes)
            {
                foreach (var attribute in element.Attributes())
                {
                    var attributePath = string.Format("{0}.{1}", path, CaptalizeString(attribute.Name.LocalName));
                    if (!fields.Contains(attributePath))
                        fields.Add(attributePath);
                }
            }
        }

        /// <summary>
        /// Gets the property information for an element, for some element name is not the same as property name, i.e. Mongo field name.
        /// </summary>
        /// <param name="properties">The properties.</param>
        /// <param name="element">The element.</param>
        /// <returns>The found property for the serialization element.</returns>
        private PropertyInfo GetPropertyInfoForAnElement(IEnumerable<PropertyInfo> properties, XElement element)
        {
            foreach (var prop in properties)
            {
                var elementAttribute = prop.GetCustomAttribute(typeof(XmlElementAttribute), false) as XmlElementAttribute;
                if (elementAttribute != null)
                {
                    if (elementAttribute.ElementName == element.Name.LocalName)
                        return prop;
                }

                var attributeAttribute = prop.GetCustomAttribute(typeof(XmlAttributeAttribute), false) as XmlAttributeAttribute;
                if (attributeAttribute != null)
                {
                    if (attributeAttribute.AttributeName == element.Name.LocalName)
                        return prop;
                }
            }
            return null;
        }

        private string CaptalizeString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = char.ToUpper(input[0]).ToString();
            if (input.Length > 1)
                result += input.Substring(1);

            return result;
        }
    }
}
