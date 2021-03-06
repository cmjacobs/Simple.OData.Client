﻿using System;
using System.Collections.Generic;
using System.Linq;
using Simple.OData.Client.Extensions;

namespace Simple.OData.Client
{
    public partial class ODataExpression
    {
        internal static int ArgumentCounter = 0;

        internal string Format(ExpressionContext context)
        {
            if (context.IsQueryOption && _operator != ExpressionOperator.None && 
                _operator != ExpressionOperator.AND && _operator != ExpressionOperator.EQ)
            {
                throw new InvalidOperationException("Invalid custom query option");
            }

            if (_operator == ExpressionOperator.None && _conversionType == null)
            {
                return this.Reference != null ?
                    FormatReference(context) : this.Function != null ?
                    FormatFunction(context) :
                    FormatValue(context);
            }
            else if (_conversionType != null)
            {
                var expr = _left;
                if (expr.Reference == null && expr.Function == null && expr._conversionType == null)
                {
                    object result;
                    if (expr.Value != null && expr.Value.GetType().IsEnumType())
                    {
                        expr = new ODataExpression(expr.Value);
                    }
                    else if (Utils.TryConvert(expr.Value, _conversionType, out result))
                    {
                        expr = new ODataExpression(result);
                    }
                }
                return FormatExpression(expr, context);
            }
            else if (_operator == ExpressionOperator.NOT || _operator == ExpressionOperator.NEG)
            {
                var left = FormatExpression(_left, context);
                var op = FormatOperator(context);
                if (NeedsGrouping(_left))
                    return string.Format("{0}({1})", op, left);
                else
                    return string.Format("{0} {1}", op, left);
            }
            else
            {
                var left = FormatExpression(_left, context);
                var right = FormatExpression(_right, context);
                var op = FormatOperator(context);

                if (context.IsQueryOption)
                {
                    return string.Format("{0}{1}{2}", left, op, right);
                }
                else
                {
                    if (NeedsGrouping(_left))
                        return string.Format("({0}) {1} {2}", left, op, right);
                    else if (NeedsGrouping(_right))
                        return string.Format("{0} {1} ({2})", left, op, right);
                    else
                        return string.Format("{0} {1} {2}", left, op, right);
                }
            }
        }

        private static string FormatExpression(ODataExpression expr, ExpressionContext context)
        {
            if (ReferenceEquals(expr, null))
            {
                return "null";
            }
            else
            {
                return expr.Format(context);
            }
        }

        private string FormatReference(ExpressionContext context)
        {
            var elementNames = new List<string>(this.Reference.Split('.', '/'));
            var entityCollection = context.EntityCollection;
            var segmentNames = BuildReferencePath(new List<string>(), entityCollection, elementNames, context);
            return FormatScope(string.Join("/", segmentNames), context);
        }

        private string FormatFunction(ExpressionContext context)
        {
            FunctionMapping mapping;
            var adapterVersion = context.Session == null ? AdapterVersion.Default : context.Session.Adapter.AdapterVersion;
            if (FunctionMapping.TryGetFunctionMapping(this.Function.FunctionName, this.Function.Arguments.Count(), adapterVersion, out mapping))
            {
                return FormatMappedFunction(context, mapping);
            }
            else if (string.Equals(this.Function.FunctionName, ODataLiteral.Any, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(this.Function.FunctionName, ODataLiteral.All, StringComparison.OrdinalIgnoreCase))
            {
                return FormatAnyAllFunction(context);
            }
            else if (string.Equals(this.Function.FunctionName, ODataLiteral.IsOf, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(this.Function.FunctionName, ODataLiteral.Cast, StringComparison.OrdinalIgnoreCase))
            {
                return FormatIsOfCastFunction(context);
            }
            else if (string.Equals(this.Function.FunctionName, "get_Item", StringComparison.Ordinal) &&
                this.Function.Arguments.Count == 1)
            {
                return FormatArrayIndexFunction(context);
            }
            else if (string.Equals(this.Function.FunctionName, "ToString", StringComparison.Ordinal) &&
                this.Function.Arguments.Count == 0)
            {
                return _functionCaller.Reference;
            }
            else
            {
                throw new NotSupportedException(string.Format("The function {0} is not supported or called with wrong number of arguments", this.Function.FunctionName));
            }
        }

        private string FormatMappedFunction(ExpressionContext context, FunctionMapping mapping)
        {
            var mappedFunction = mapping.FunctionMapper(
                this.Function.FunctionName, _functionCaller, this.Function.Arguments).Function;
            var formattedArguments = string.Join(",",
                (IEnumerable<object>)mappedFunction.Arguments.Select(x => FormatExpression(x, context)));

            return string.Format("{0}({1})",
                mappedFunction.FunctionName,
                formattedArguments);
        }

        private string FormatAnyAllFunction(ExpressionContext context)
        {
            var navigationPath = _functionCaller.Reference.Replace('.', '/');
            var entityCollection = NavigateToCollection(context,
                context.EntityCollection, navigationPath);

            var targetQualifier = string.Format("x{0}", ArgumentCounter >= 0 ? (1 + (ArgumentCounter++) % 9).ToString() : string.Empty);
            var formattedArguments = string.Format("{0}:{1}",
                targetQualifier,
                FormatExpression(this.Function.Arguments.First(), new ExpressionContext(context.Session,
                    entityCollection, targetQualifier, context.DynamicPropertiesContainerName)));

            return FormatScope(
                string.Format("{0}/{1}({2})",
                    context.Session.Adapter.GetCommandFormatter().FormatNavigationPath(context.EntityCollection, navigationPath),
                    this.Function.FunctionName.ToLower(),
                formattedArguments), context);
        }

        private EntityCollection NavigateToCollection(ExpressionContext context, EntityCollection rootCollection, string path)
        {
            var items = path.Split('/');
            var associationName = context.Session.Metadata.GetNavigationPropertyExactName(rootCollection.Name, items.First());

            var entityCollection = context.Session.Metadata.GetEntityCollection(
                context.Session.Metadata.GetNavigationPropertyPartnerName(rootCollection.Name, associationName));

            if (items.Count() == 1)
            {
                return entityCollection;
            }
            else
            {
                path = path.Substring(items.First().Length + 1);
                return NavigateToCollection(context, entityCollection, path);
            }
        }

        private string FormatIsOfCastFunction(ExpressionContext context)
        {
            var formattedArguments = string.Empty;
            if (!ReferenceEquals(this.Function.Arguments.First(), null) && !this.Function.Arguments.First().IsNull)
            {
                formattedArguments += FormatExpression(this.Function.Arguments.First(), new ExpressionContext(context.Session));
                formattedArguments += ",";
            }
            formattedArguments += FormatExpression(this.Function.Arguments.Last(), new ExpressionContext(context.Session));

            return string.Format("{0}({1})",
                this.Function.FunctionName.ToLower(), formattedArguments);
        }

        private string FormatArrayIndexFunction(ExpressionContext context)
        {
            var propertyName =
                FormatExpression(this.Function.Arguments.First(), new ExpressionContext(context.Session)).Trim('\'');
            return _functionCaller.Reference == context.DynamicPropertiesContainerName
                ? propertyName
                : string.Format("{0}.{1}", _functionCaller.Reference, propertyName);
        }

        private string FormatValue(ExpressionContext context)
        {
            if (Value is ODataExpression)
            {
                return (Value as ODataExpression).Format(context);
            }
            else if (Value is Type)
            {
                var typeName = context.Session.Adapter.GetMetadata().GetQualifiedTypeName((Value as Type).Name);
                return context.Session.Adapter.GetCommandFormatter().ConvertValueToUriLiteral(typeName, false);
            }
            else
            {
                return context.Session.Adapter.GetCommandFormatter().ConvertValueToUriLiteral(Value, false);
            }
        }

        private string FormatOperator(ExpressionContext context)
        {
            switch (_operator)
            {
                case ExpressionOperator.AND:
                    return context.IsQueryOption ? "&" : "and";
                case ExpressionOperator.OR:
                    return "or";
                case ExpressionOperator.NOT:
                    return "not";
                case ExpressionOperator.EQ:
                    return context.IsQueryOption ? "=" : "eq";
                case ExpressionOperator.NE:
                    return "ne";
                case ExpressionOperator.GT:
                    return "gt";
                case ExpressionOperator.GE:
                    return "ge";
                case ExpressionOperator.LT:
                    return "lt";
                case ExpressionOperator.LE:
                    return "le";
                case ExpressionOperator.ADD:
                    return "add";
                case ExpressionOperator.SUB:
                    return "sub";
                case ExpressionOperator.MUL:
                    return "mul";
                case ExpressionOperator.DIV:
                    return "div";
                case ExpressionOperator.MOD:
                    return "mod";
                case ExpressionOperator.NEG:
                    return "-";
                default:
                    return null;
            }
        }

        private IEnumerable<string> BuildReferencePath(List<string> segmentNames, EntityCollection entityCollection, List<string> elementNames, ExpressionContext context)
        {
            if (!elementNames.Any())
            {
                return segmentNames;
            }

            var objectName = elementNames.First();
            if (entityCollection != null)
            {
                if (context.Session.Metadata.HasStructuralProperty(entityCollection.Name, objectName))
                {
                    var propertyName = context.Session.Metadata.GetStructuralPropertyExactName(
                        entityCollection.Name, objectName);
                    segmentNames.Add(propertyName);
                    return BuildReferencePath(segmentNames, null, elementNames.Skip(1).ToList(), context);
                }
                else if (context.Session.Metadata.HasNavigationProperty(entityCollection.Name, objectName))
                {
                    var propertyName = context.Session.Metadata.GetNavigationPropertyExactName(
                        entityCollection.Name, objectName);
                    var linkName = context.Session.Metadata.GetNavigationPropertyPartnerName(
                        entityCollection.Name, objectName);
                    var linkedEntityCollection = context.Session.Metadata.GetEntityCollection(linkName);
                    segmentNames.Add(propertyName);
                    return BuildReferencePath(segmentNames, linkedEntityCollection, elementNames.Skip(1).ToList(), context);
                }
                else if (IsFunction(objectName, context))
                {
                    var formattedFunction = FormatAsFunction(objectName, context);
                    segmentNames.Add(formattedFunction);
                    return BuildReferencePath(segmentNames, null, elementNames.Skip(1).ToList(), context);
                }
                else if (context.Session.Metadata.IsOpenType(entityCollection.Name))
                {
                    segmentNames.Add(objectName);
                    return BuildReferencePath(segmentNames, null, elementNames.Skip(1).ToList(), context);
                }
                else
                {
                    throw new UnresolvableObjectException(objectName, string.Format("Invalid referenced object [{0}]", objectName));
                }
            }
            else if (FunctionMapping.ContainsFunction(elementNames.First(), 0))
            {
                var formattedFunction = FormatAsFunction(objectName, context);
                segmentNames.Add(formattedFunction);
                return BuildReferencePath(segmentNames, null, elementNames.Skip(1).ToList(), context);
            }
            else
            {
                segmentNames.AddRange(elementNames);
                return BuildReferencePath(segmentNames, null, new List<string>(), context);
            }
        }

        private bool IsFunction(string objectName, ExpressionContext context)
        {
            FunctionMapping mapping;
            var adapterVersion = context.Session == null ? AdapterVersion.Default : context.Session.Adapter.AdapterVersion;
            return FunctionMapping.TryGetFunctionMapping(objectName, 0, adapterVersion, out mapping);
        }

        private string FormatAsFunction(string objectName, ExpressionContext context)
        {
            FunctionMapping mapping;
            var adapterVersion = context.Session == null ? AdapterVersion.Default : context.Session.Adapter.AdapterVersion;
            if (FunctionMapping.TryGetFunctionMapping(objectName, 0, adapterVersion, out mapping))
            {
                var mappedFunction = mapping.FunctionMapper(objectName, _functionCaller, null).Function;
                return string.Format("{0}({1})", mappedFunction.FunctionName, _functionCaller.Format(context));
            }
            else
            {
                return null;
            }
        }

        private int GetPrecedence(ExpressionOperator op)
        {
            switch (op)
            {
                case ExpressionOperator.NOT:
                case ExpressionOperator.NEG:
                    return 1;
                case ExpressionOperator.MOD:
                case ExpressionOperator.MUL:
                case ExpressionOperator.DIV:
                    return 2;
                case ExpressionOperator.ADD:
                case ExpressionOperator.SUB:
                    return 3;
                case ExpressionOperator.GT:
                case ExpressionOperator.GE:
                case ExpressionOperator.LT:
                case ExpressionOperator.LE:
                    return 4;
                case ExpressionOperator.EQ:
                case ExpressionOperator.NE:
                    return 5;
                case ExpressionOperator.AND:
                    return 6;
                case ExpressionOperator.OR:
                    return 7;
                default:
                    return 0;
            }
        }

        private bool NeedsGrouping(ODataExpression expr)
        {
            if (_operator == ExpressionOperator.None)
                return false;
            if (ReferenceEquals(expr, null))
                return false;
            if (expr._operator == ExpressionOperator.None)
                return false;

            int outerPrecedence = GetPrecedence(_operator);
            int innerPrecedence = GetPrecedence(expr._operator);
            return outerPrecedence < innerPrecedence;
        }

        private string FormatScope(string text, ExpressionContext context)
        {
            return string.IsNullOrEmpty(context.ScopeQualifier)
                ? text
                : string.Format("{0}/{1}", context.ScopeQualifier, text);
        }
    }
}
