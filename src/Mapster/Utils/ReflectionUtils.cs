﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Mapster.Utils;

namespace Mapster
{
    internal static class ReflectionUtils
    {
        private static readonly Type _stringType = typeof(string);

#if NET4
        public static Type GetTypeInfo(this Type type) {
            return type;
        }
#endif

        public static bool IsNullable(this Type type)
        {
            return type.GetTypeInfo().IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        public static List<MemberInfo> GetPublicFieldsAndProperties(this Type type, bool allowNonPublicSetter = true, bool allowNoSetter = true)
        {
            var results = new List<MemberInfo>();

            results.AddRange(type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(x => (allowNoSetter || x.CanWrite) && (allowNonPublicSetter || x.GetSetMethod() != null)));

            results.AddRange(type.GetFields(BindingFlags.Instance | BindingFlags.Public).Where(x => (allowNoSetter || x.IsInitOnly)));

            return results;
        }

        public static MemberInfo GetPublicFieldOrProperty(Type type, bool isProperty, string name)
        {
            if (isProperty)
                return type.GetProperty(name);
            
            return type.GetField(name);
        }

        public static Type GetMemberType(this MemberInfo mi)
        {
            var pi = mi as PropertyInfo;
            if (pi != null)
            {
                return pi.PropertyType;
            }

            var fi = mi as FieldInfo;
            if (fi != null)
            {
                return fi.FieldType;
            }

            var mti = mi as MethodInfo;
            return mti?.ReturnType;
        }

        public static bool HasPublicSetter(this MemberInfo mi)
        {
            var pi = mi as PropertyInfo;
            if (pi != null)
            {
                return pi.GetSetMethod() != null;
            }

            var fi = mi as FieldInfo;
            if (fi != null)
            {
                return fi.IsPublic;
            }
            return false;
        }

        public static bool IsCollection(this Type type)
        {
            return typeof(IEnumerable).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()) && type != _stringType;
        }

        public static Type ExtractCollectionType(this Type collectionType)
        {
            if (collectionType.IsGenericEnumerableType())
            {
                return collectionType.GetGenericArguments()[0];
            }
            var enumerableType = collectionType.GetInterfaces().FirstOrDefault(IsGenericEnumerableType);
            if (enumerableType != null)
            {
                return enumerableType.GetGenericArguments()[0];
            }
            return typeof (object);
        }

        public static bool IsGenericEnumerableType(this Type type)
        {
            return type.GetTypeInfo().IsGenericType && type.GetGenericTypeDefinition() == typeof (IEnumerable<>);
        }

        private static Expression CreateConvertMethod(string name, Type srcType, Type destType, Expression source)
        {
            var method = typeof (Convert).GetMethod(name, new[] {srcType});
            if (method != null)
                return Expression.Call(method, source);

            method = typeof (Convert).GetMethod(name, new[] {typeof (object)});
            return Expression.Convert(Expression.Call(method, Expression.Convert(source, typeof (object))), destType);
        }

        public static object GetDefault(this Type type)
        {
            return type.GetTypeInfo().IsValueType && !type.IsNullable()
                ? Activator.CreateInstance(type)
                : null;
        }

        public static Expression BuildUnderlyingTypeConvertExpression(Expression source, Type sourceType, Type destinationType)
        {
            var srcType = sourceType.IsNullable() ? sourceType.GetGenericArguments()[0] : sourceType;
            var destType = destinationType.IsNullable() ? destinationType.GetGenericArguments()[0] : destinationType;
            
            if (srcType == destType)
                return source;

            //special handling for string
            if (destType == _stringType)
            {
                if (srcType.GetTypeInfo().IsEnum)
                {
                    var method = typeof(Enum<>).MakeGenericType(srcType).GetMethod("ToString", new[] { srcType });
                    return Expression.Call(method, source);
                }
                else
                {
                    var method = srcType.GetMethod("ToString", Type.EmptyTypes);
                    return Expression.Call(source, method);
                }
            }

            if (srcType == _stringType)
            {
                if (destType.GetTypeInfo().IsEnum)
                {
                    var method = typeof(Enum<>).MakeGenericType(destType).GetMethod("Parse", new[] { typeof(string) });
                    return Expression.Call(method, source);
                }
                else
                {
                    var method = destType.GetMethod("Parse", new[] { typeof(string) });
                    if (method != null)
                        return Expression.Call(method, source);
                }
            }

            //try using type casting
            try
            {
                return Expression.Convert(source, destType);
            }
            catch { }

            if (srcType.GetInterfaces().All(type => type != typeof (IConvertible)))
                throw new InvalidOperationException(
                    $"Cannot convert immutable type, please consider using 'MapWith' method to create mapping: TSource: {sourceType} TDestination: {destinationType}");

            //using Convert
            if (destType == typeof(bool))
                return CreateConvertMethod("ToBoolean", srcType, destType, source);

            if (destType == typeof(int))
                return CreateConvertMethod("ToInt32", srcType, destType, source);

            if (destType == typeof(long))
                return CreateConvertMethod("ToInt64", srcType, destType, source);

            if (destType == typeof(short))
                return CreateConvertMethod("ToInt16", srcType, destType, source);

            if (destType == typeof(decimal))
                return CreateConvertMethod("ToDecimal", srcType, destType, source);

            if (destType == typeof(double))
                return CreateConvertMethod("ToDouble", srcType, destType, source);

            if (destType == typeof(float))
                return CreateConvertMethod("ToSingle", srcType, destType, source);

            if (destType == typeof(DateTime))
                return CreateConvertMethod("ToDateTime", srcType, destType, source);

            if (destType == typeof(ulong))
                return CreateConvertMethod("ToUInt64", srcType, destType, source);

            if (destType == typeof(uint))
                return CreateConvertMethod("ToUInt32", srcType, destType, source);

            if (destType == typeof(ushort))
                return CreateConvertMethod("ToUInt16", srcType, destType, source);

            if (destType == typeof(byte))
                return CreateConvertMethod("ToByte", srcType, destType, source);

            if (destType == typeof(sbyte))
                return CreateConvertMethod("ToSByte", srcType, destType, source);

            var changeTypeMethod = typeof(Convert).GetMethod("ChangeType", new[] { typeof(object), typeof(Type) });
            return Expression.Convert(Expression.Call(changeTypeMethod, Expression.Convert(source, typeof(object)), Expression.Constant(destType)), destType);
        }

        public static MemberExpression GetMemberInfo(Expression method)
        {
            var lambda = method as LambdaExpression;
            if (lambda == null)
                throw new ArgumentNullException(nameof(method));

            MemberExpression memberExpr = null;

            if (lambda.Body.NodeType == ExpressionType.Convert)
            {
                memberExpr =
                    ((UnaryExpression)lambda.Body).Operand as MemberExpression;
            }
            else if (lambda.Body.NodeType == ExpressionType.MemberAccess)
            {
                memberExpr = lambda.Body as MemberExpression;
            }

            if (memberExpr == null)
                throw new ArgumentException("argument must be member access", nameof(method));

            return memberExpr;
        }

        public static Expression GetDeepFlattening(Expression source, string propertyName, bool isProjection)
        {
            var properties = source.Type.GetPublicFieldsAndProperties();
            for (int j = 0; j < properties.Count; j++)
            {
                var property = properties[j];
                var propertyType = property.GetMemberType();
                if (propertyType.GetTypeInfo().IsClass && propertyType != _stringType
                    && propertyName.StartsWith(property.Name))
                {
                    var exp = property is PropertyInfo
                        ? Expression.Property(source, (PropertyInfo) property)
                        : Expression.Field(source, (FieldInfo) property);
                    var ifTrue = GetDeepFlattening(exp, propertyName.Substring(property.Name.Length).TrimStart('_'), isProjection);
                    if (ifTrue == null)
                        return null;
                    if (isProjection)
                        return ifTrue;
                    return Expression.Condition(
                        Expression.Equal(exp, Expression.Constant(null, exp.Type)), 
                        Expression.Constant(ifTrue.Type.GetDefault(), ifTrue.Type), 
                        ifTrue);
                }
                else if (string.Equals(propertyName, property.Name))
                {
                    return property is PropertyInfo
                        ? Expression.Property(source, (PropertyInfo)property)
                        : Expression.Field(source, (FieldInfo)property);
                }
            }
            return null;
        }

        public static bool IsReferenceAssignableFrom(this Type destType, Type srcType)
        {
            if (destType == srcType)
                return true;

            if (!destType.GetTypeInfo().IsValueType && !srcType.GetTypeInfo().IsValueType && destType.GetTypeInfo().IsAssignableFrom(srcType.GetTypeInfo()))
                return true;

            return false;
        }
    }
}
