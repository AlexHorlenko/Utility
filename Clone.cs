using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Utility
{
    public static class Clone
    {
        private static readonly ConcurrentDictionary<Type, Func<object, object>> KnownDeepCloningFuncs = new ConcurrentDictionary<Type, Func<object, object>>();

        public static object CloneDeep(object original)
        {
            if (original is null)
                return null;

            Type type = original.GetType();
            if (KnownDeepCloningFuncs.ContainsKey(type) && KnownDeepCloningFuncs.TryGetValue(type, out Func<object, object> creator)) return creator(original);

            PropertyInfo[]            propertiesArray       = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            IEnumerable<PropertyInfo> primitives            = propertiesArray.Where(x => x.PropertyType.IsValueType || x.PropertyType                                 == typeof(string));
            IEnumerable<PropertyInfo> composites            = propertiesArray.Where(x => x.PropertyType.IsClass && x.PropertyType != typeof(string) && x.PropertyType != typeof(Delegate));
            ParameterExpression       resultExpression      = Expression.Parameter(typeof(object), "result");
            ParameterExpression       inputExpression       = Expression.Parameter(typeof(object), "input");
            LabelTarget               returnTarget          = Expression.Label(typeof(object));
            GotoExpression            returnExpression      = Expression.Return(returnTarget, resultExpression, typeof(object));
            LabelExpression           returnLabel           = Expression.Label(returnTarget, Expression.Default(typeof(object)));
            var                       variableExpressions   = new List<ParameterExpression>();
            var                       assignExpressions     = new List<Expression>();
            ParameterExpression       typedInputExpresssion = Expression.Parameter(type, "typedInput");
            ParameterExpression       typedOutputExpression = Expression.Parameter(type, "typedResult");
            variableExpressions.AddRange(new[] { resultExpression, typedOutputExpression, typedInputExpresssion });
            assignExpressions.Add(Expression.Assign(typedInputExpresssion, Expression.Convert(inputExpression, type)));
            assignExpressions.Add(Expression.Assign(typedOutputExpression, Expression.New(type)));
            assignExpressions.AddRange(GetCopyPropertiesExpressions(typedInputExpresssion, typedOutputExpression, primitives));
            foreach (PropertyInfo composite in composites)
            {
                ParameterExpression inParameter  = Expression.Parameter(composite.PropertyType, $"in{composite.Name}");
                ParameterExpression outParameter = Expression.Parameter(composite.PropertyType, $"out{composite.Name}");
                variableExpressions.AddRange(new[] { inParameter, outParameter });
                assignExpressions.Add(Expression.Assign(inParameter,  Expression.Property(typedInputExpresssion, composite.Name)));
                assignExpressions.Add(GetCopyObjectExpression(composite.PropertyType, inParameter, outParameter, out var internalVariables));
                variableExpressions.AddRange(internalVariables);
                assignExpressions.Add(Expression.Assign(Expression.Property(typedOutputExpression, composite.Name), outParameter));
            }

            Expression creatorExpression = Expression.Block(
                typeof(object),
                variableExpressions,
                assignExpressions.Concat(new Expression[] { Expression.Assign(resultExpression, typedOutputExpression), returnExpression, returnLabel }));
            Func<object, object> creatorFunction = Expression.Lambda<Func<object, object>>(creatorExpression, inputExpression).Compile();
            KnownDeepCloningFuncs.AddOrUpdate(type, creatorFunction, (typ, func) => func);
            return creatorFunction(original);
        }

        private static Expression GetCopyObjectExpression(Type type, ParameterExpression sourceParameter, ParameterExpression targetParameter, out List<ParameterExpression> variables)
        {
            var                       assignments     = new List<Expression>();
            PropertyInfo[]            propertiesArray = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            IEnumerable<PropertyInfo> primitives      = propertiesArray.Where(x => x.PropertyType.IsValueType || x.PropertyType                                 == typeof(string));
            IEnumerable<PropertyInfo> composites      = propertiesArray.Where(x => x.PropertyType.IsClass && x.PropertyType != typeof(string) && x.PropertyType != typeof(Delegate));
            variables = new List<ParameterExpression>();
            
            assignments.Add(Expression.Assign(targetParameter, Expression.New(type)));
            assignments.AddRange(GetCopyPropertiesExpressions(sourceParameter, targetParameter, primitives));
            foreach (PropertyInfo composite in composites)
            {
                ParameterExpression inParameter  = Expression.Parameter(composite.PropertyType, $"in{composite.Name}");
                ParameterExpression outParameter = Expression.Parameter(composite.PropertyType, $"out{composite.Name}");
                variables.AddRange(new[] { inParameter, outParameter });
                assignments.Add(Expression.Assign(inParameter,  Expression.Property(sourceParameter, composite.Name)));
                assignments.Add(GetCopyObjectExpression(composite.PropertyType, inParameter, outParameter,out var internalVariables));
                assignments.Add(Expression.Assign(Expression.Property(targetParameter, composite.Name), outParameter));
                variables.AddRange(internalVariables);
            }
            
            return Expression.IfThen(Expression.NotEqual(sourceParameter, Expression.Constant(null, type)), Expression.Block(assignments));
        }

        private static IEnumerable<Expression> GetCopyPropertiesExpressions(ParameterExpression sourceParameter, ParameterExpression targetParameter, IEnumerable<PropertyInfo> properties)
        {
            foreach (PropertyInfo property in properties)
            {
                MemberExpression sourceSelector = Expression.Property(sourceParameter, property.Name);
                MemberExpression targetSelector = Expression.Property(targetParameter, property.Name);
                yield return Expression.Assign(targetSelector, sourceSelector);
            }
        }
    }
}
