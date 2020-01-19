﻿// The MIT License (MIT)
// 
// Copyright (c) 2015-2018 Rasmus Mikkelsen
// Copyright (c) 2015-2018 eBay Software Foundation
// https://github.com/eventflow/EventFlow
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EventFlow.Aggregates;
using EventFlow.Core;

namespace EventFlow.ReadStores
{
    public class ReadModelDomainEventApplier : IReadModelDomainEventApplier
    {
        private const string ApplyAsyncMethodName = "ApplyAsync";

        private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Type, ApplyMethod>> _applyMethods =
            new ConcurrentDictionary<Type, ConcurrentDictionary<Type, ApplyMethod>>();

        public async Task<bool> UpdateReadModelAsync<TReadModel>(
            TReadModel readModel,
            IReadOnlyCollection<IDomainEvent> domainEvents,
            IReadModelContext readModelContext,
            CancellationToken cancellationToken)
            where TReadModel : IReadModel
        {
            var readModelType = typeof(TReadModel);
            var appliedAny = false;

            foreach (var domainEvent in domainEvents)
            {
                var applyMethods = _applyMethods.GetOrAdd(
                    readModelType,
                    t => new ConcurrentDictionary<Type, ApplyMethod>());
                var applyMethod = applyMethods.GetOrAdd(
                    domainEvent.EventType,
                    t =>
                    {
                        var domainEventType = typeof(IDomainEvent<,,>).MakeGenericType(
                            domainEvent.AggregateType,
                            domainEvent.GetIdentity().GetType(), t);

                        var asyncMethodSignature = new[] {typeof(IReadModelContext), domainEventType, typeof(CancellationToken)};
                        var methodInfo = readModelType.GetTypeInfo().GetMethod(ApplyAsyncMethodName, asyncMethodSignature);

                        if (methodInfo == null)
                        {
                            return null;
                        }

                        var method = ReflectionHelper.CompileMethodInvocation<Func<IReadModel, IReadModelContext, IDomainEvent, CancellationToken, Task>>(methodInfo);

                        return new ApplyMethod(method);

                    });

                if (applyMethod == null)
                {
                    continue;
                }

                await applyMethod.ApplyAsync(readModel, readModelContext, domainEvent, cancellationToken).ConfigureAwait(false);
                
                appliedAny = true;
            }

            return appliedAny;
        }

        private class ApplyMethod
        {
            private readonly Func<IReadModel, IReadModelContext, IDomainEvent, CancellationToken, Task> _method;

            public ApplyMethod(Func<IReadModel, IReadModelContext, IDomainEvent, CancellationToken, Task> method)
            {
                _method = method;
            }

            public Task ApplyAsync(
                IReadModel readModel,
                IReadModelContext context,
                IDomainEvent domainEvent,
                CancellationToken cancellationToken)
            {
                return _method(readModel, context, domainEvent, cancellationToken);
            }
        }
    }
}
