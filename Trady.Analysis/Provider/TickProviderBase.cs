﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Trady.Analysis.Indicator;
using Trady.Analysis.Pattern;
using Trady.Core;

namespace Trady.Analysis.Provider
{
    public abstract class TickProviderBase : ITickProvider
    {
        public abstract bool IsReady { get; }
        protected object[] _parameters;
        protected IAnalyzable _analyzable;

        public TickProviderBase(params object[] parameters)
        {
            _parameters = parameters;
        }

        public ITickProvider Clone()
        {
            var ctor = GetType().GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).First();
            return (ITickProvider)ctor.Invoke(_parameters);
        }

        public async Task<IEnumerable<TTick>> GetAllAsync<TTick>() where TTick : ITick
        {
            if (IsReady)
            {
                var tickParamType = GetParameterUnderlyingType(typeof(TTick));
                if (tickParamType == null)
                    throw new ArgumentException("TTick is not inherited from IndicatorResultBase nor PatternResult<>, TTick instance cannot be constructed");

                var returnTicks = new List<TTick>();
                var propTicks = await GetPropertyTicks().ConfigureAwait(false);
                if (!propTicks.Any())
                    return returnTicks;

                var propTickGroups = propTicks.GroupBy(t => t.DateTime);
                var ctor = typeof(TTick).GetConstructors(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance).First();
                foreach (var propTickGroup in propTickGroups)   // Loop by datetime
                {
                    var args = new List<object> { propTickGroup.Key };
                    foreach (var @param in ctor.GetParameters())    // Loop by TTick ctor parameters
                    {
                        // Compare parameter name with IndicatorValue name field, add to arguments if matched
                        if (@param.Name.Equals("datetime", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var propTick = propTickGroup.FirstOrDefault(t => @param.Name.Replace("@", "").Equals(t.Name, StringComparison.OrdinalIgnoreCase));
                        if (propTick != null)
                        {
                            object obj = null;
                            if (propTick.Value != null)
                                obj = tickParamType.GetTypeInfo().IsEnum ?
                                    Enum.ToObject(tickParamType, propTick.Value) :
                                    Convert.ChangeType(propTick.Value, tickParamType);
                            args.Add(obj);
                        }
                    }

                    // Construct and add TTick
                    var tick = (TTick)ctor.Invoke(args.ToArray());
                    returnTicks.Add(tick);
                }

                return returnTicks;
            }
            return new List<TTick>();
        }

        protected abstract Task<IEnumerable<IPropertyTick>> GetPropertyTicks();

        private static Type GetParameterUnderlyingType(Type tickType)
        {
            if (tickType.GetTypeInfo().IsSubclassOf(typeof(IndicatorResultBase)))
                return typeof(decimal);
            else if (tickType.GetTypeInfo().IsSubclassOf(typeof(PatternResult<>)))
            {
                var baseType = tickType.GetTypeInfo().BaseType;
                while (baseType != null && baseType != typeof(object))
                {
                    if (baseType.GetTypeInfo().IsGenericType)
                    {
                        var resultGenericType = baseType.GenericTypeArguments[0];
                        if (resultGenericType.GetTypeInfo().IsGenericType && resultGenericType.GetTypeInfo().GetGenericTypeDefinition() == typeof(Nullable<>))
                            resultGenericType = Nullable.GetUnderlyingType(resultGenericType);
                        return resultGenericType;
                    }

                    baseType = baseType.GetTypeInfo().BaseType;
                }
            }
            return null;
        }

        public async virtual Task InitWithAnalyzableAsync(IAnalyzable analyzable)
        {
            _analyzable = analyzable;
            // Implement your own logic here
        }
    }
}