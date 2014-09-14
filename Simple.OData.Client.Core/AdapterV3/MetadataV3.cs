﻿using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Edm;
using Simple.OData.Client.Extensions;

namespace Simple.OData.Client
{
    class MetadataV3 : IMetadata
    {
        private readonly ISession _session;
        private readonly IEdmModel _model;

        public MetadataV3(ISession session, IEdmModel model)
        {
            _session = session;
            _model = model;
        }

        public IEnumerable<string> GetEntitySetNames()
        {
            return GetEntitySets().Select(x => x.Name);
        }

        public string GetEntitySetExactName(string entitySetName)
        {
            return GetEntitySet(entitySetName).Name;
        }

        public string GetEntitySetTypeName(string entitySetName)
        {
            return GetEntityType(entitySetName).Name;
        }

        public string GetEntitySetTypeNamespace(string entitySetName)
        {
            return GetEntityType(entitySetName).Namespace;
        }

        public bool EntitySetTypeRequiresOptimisticConcurrencyCheck(string entitySetName)
        {
            return GetEntityType(entitySetName).StructuralProperties()
                .Any(x => x.ConcurrencyMode == EdmConcurrencyMode.Fixed);
        }

        public string GetDerivedEntityTypeExactName(string entitySetName, string entityTypeName)
        {
            var entitySet = GetEntitySet(entitySetName);
            var entityType = (_model.FindDirectlyDerivedTypes(entitySet.ElementType)
                .SingleOrDefault(x => Utils.NamesAreEqual((x as IEdmEntityType).Name, entityTypeName, _session.Pluralizer)) as IEdmEntityType);

            if (entityType == null)
                throw new UnresolvableObjectException(entityTypeName, string.Format("Entity type {0} not found", entityTypeName));

            return entityType.Name;
        }

        public string GetEntityTypeExactName(string entityTypeName)
        {
            var entityType = GetEntityTypes().SingleOrDefault(x => Utils.NamesAreEqual(x.Name, entityTypeName, _session.Pluralizer));

            if (entityType == null)
                throw new UnresolvableObjectException(entityTypeName, string.Format("Entity type {0} not found", entityTypeName));

            return entityType.Name;
        }

        public EntityCollection GetEntityCollection(string entitySetName)
        {
            return new EntityCollection(GetEntitySetExactName(entitySetName));
        }

        public EntityCollection GetBaseEntityCollection(string entitySetPath)
        {
            return this.GetEntityCollection(entitySetPath.Split('/').First());
        }

        public EntityCollection GetConcreteEntityCollection(string entitySetPath)
        {
            var items = entitySetPath.Split('/');
            if (items.Count() > 1)
            {
                var baseEntitySet = this.GetEntityCollection(items[0]);
                var entitySet = string.IsNullOrEmpty(items[1])
                    ? baseEntitySet
                    : GetDerivedEntityCollection(baseEntitySet, items[1]);
                return entitySet;
            }
            else
            {
                return this.GetEntityCollection(entitySetPath);
            }
        }

        public EntityCollection GetDerivedEntityCollection(EntityCollection baseEntityCollection, string entityTypeName)
        {
            var actualName = GetDerivedEntityTypeExactName(baseEntityCollection.ActualName, entityTypeName);
            return new EntityCollection(actualName, baseEntityCollection);
        }

        public IEnumerable<string> GetStructuralPropertyNames(string entitySetName)
        {
            return GetEntityType(entitySetName).StructuralProperties().Select(x => x.Name);
        }

        public bool HasStructuralProperty(string entitySetName, string propertyName)
        {
            return GetEntityType(entitySetName).StructuralProperties().Any(x => Utils.NamesAreEqual(x.Name, propertyName, _session.Pluralizer));
        }

        public string GetStructuralPropertyExactName(string entitySetName, string propertyName)
        {
            return GetStructuralProperty(entitySetName, propertyName).Name;
        }

        public bool HasNavigationProperty(string entitySetName, string propertyName)
        {
            return GetEntityType(entitySetName).NavigationProperties().Any(x => Utils.NamesAreEqual(x.Name, propertyName, _session.Pluralizer));
        }

        public string GetNavigationPropertyExactName(string entitySetName, string propertyName)
        {
            return GetNavigationProperty(entitySetName, propertyName).Name;
        }

        public string GetNavigationPropertyPartnerName(string entitySetName, string propertyName)
        {
            return (GetNavigationProperty(entitySetName, propertyName).Partner.DeclaringType as IEdmEntityType).Name;
        }

        public bool IsNavigationPropertyMultiple(string entitySetName, string propertyName)
        {
            return GetNavigationProperty(entitySetName, propertyName).Partner.Multiplicity() == EdmMultiplicity.Many;
        }

        public IEnumerable<string> GetDeclaredKeyPropertyNames(string entitySetName)
        {
            var entityType = GetEntityType(entitySetName);
            while (entityType.DeclaredKey == null && entityType.BaseEntityType() != null)
            {
                entityType = entityType.BaseEntityType();
            }

            if (entityType.DeclaredKey == null)
                return new string[] { };

            return entityType.DeclaredKey.Select(x => x.Name);
        }

        public string GetFunctionExactName(string functionName)
        {
            var function = _model.SchemaElements
                .Where(x => x.SchemaElementKind == EdmSchemaElementKind.EntityContainer)
                .SelectMany(x => (x as IEdmEntityContainer).FunctionImports()
                    .Where(y => y.Name.Homogenize() == functionName.Homogenize()))
                .SingleOrDefault();

            if (function == null)
                throw new UnresolvableObjectException(functionName, string.Format("Function {0} not found", functionName));

            return function.Name;
        }

        private IEnumerable<IEdmEntitySet> GetEntitySets()
        {
            return _model.SchemaElements
                .Where(x => x.SchemaElementKind == EdmSchemaElementKind.EntityContainer)
                .SelectMany(x => (x as IEdmEntityContainer).EntitySets());
        }

        private IEdmEntitySet GetEntitySet(string entitySetName)
        {
            if (entitySetName.Contains("/"))
                entitySetName = entitySetName.Split('/').First();

            var entitySet = _model.SchemaElements
                .Where(x => x.SchemaElementKind == EdmSchemaElementKind.EntityContainer)
                .SelectMany(x => (x as IEdmEntityContainer).EntitySets())
                .SingleOrDefault(x => Utils.NamesAreEqual(x.Name, entitySetName, _session.Pluralizer));

            if (entitySet == null)
                throw new UnresolvableObjectException(entitySetName, string.Format("Entity set {0} not found", entitySetName));

            return entitySet;
        }

        private IEnumerable<IEdmEntityType> GetEntityTypes()
        {
            return _model.SchemaElements
                .Where(x => x.SchemaElementKind == EdmSchemaElementKind.TypeDefinition && (x as IEdmType).TypeKind == EdmTypeKind.Entity)
                .Select(x => x as IEdmEntityType);
        }

        private IEdmEntityType GetEntityType(string entitySetName)
        {
            if (entitySetName.Contains("/"))
            {
                var items = entitySetName.Split('/');
                entitySetName = items.First();
                var derivedTypeName = items.Last();

                var entitySet = GetEntitySets()
                    .SingleOrDefault(x => Utils.NamesAreEqual(x.Name, entitySetName, _session.Pluralizer));

                if (entitySet != null)
                {
                    var derivedType = GetEntityTypes().SingleOrDefault(x => Utils.NamesAreEqual(x.Name, derivedTypeName, _session.Pluralizer));
                    if (derivedType != null)
                    {
                        if (_model.FindDirectlyDerivedTypes(entitySet.ElementType).Contains(derivedType))
                            return derivedType;
                    }
                }

                throw new UnresolvableObjectException(entitySetName, string.Format("Entity set {0} not found", entitySetName));
            }
            else
            {
                var entitySet = GetEntitySets()
                    .SingleOrDefault(x => Utils.NamesAreEqual(x.Name, entitySetName, _session.Pluralizer));

                if (entitySet == null)
                {
                    var derivedType = GetEntityTypes().SingleOrDefault(x => Utils.NamesAreEqual(x.Name, entitySetName, _session.Pluralizer));
                    if (derivedType != null)
                    {
                        var baseType = GetEntityTypes()
                            .SingleOrDefault(x => _model.FindDirectlyDerivedTypes(x).Contains(derivedType));
                        if (baseType != null && GetEntitySets().SingleOrDefault(x => x.ElementType == baseType) != null)
                            return derivedType;
                    }
                }

                if (entitySet == null)
                    throw new UnresolvableObjectException(entitySetName, string.Format("Entity set {0} not found", entitySetName));

                return entitySet.ElementType;
            }
        }

        private IEdmStructuralProperty GetStructuralProperty(string entitySetName, string propertyName)
        {
            var property = GetEntityType(entitySetName).StructuralProperties().SingleOrDefault(
                x => Utils.NamesAreEqual(x.Name, propertyName, _session.Pluralizer));

            if (property == null)
                throw new UnresolvableObjectException(propertyName, string.Format("Structural property {0} not found", propertyName));

            return property;
        }

        private IEdmNavigationProperty GetNavigationProperty(string entitySetName, string propertyName)
        {
            var property = GetEntityType(entitySetName).NavigationProperties().SingleOrDefault(
                x => Utils.NamesAreEqual(x.Name, propertyName, _session.Pluralizer));

            if (property == null)
                throw new UnresolvableObjectException(propertyName, string.Format("Navigation property {0} not found", propertyName));

            return property;
        }
    }
}