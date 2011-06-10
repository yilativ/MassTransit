﻿// Copyright 2007-2011 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use 
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed 
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit.NHibernateIntegration.Saga
{
	using System;
	using System.Collections.Generic;
	using Exceptions;
	using log4net;
	using MassTransit.Saga;
	using NHibernate;
	using Pipeline;
	using Util;

	public class NHibernateSagaStorage<TSaga> :
		ISagaStorage<TSaga>
		where TSaga : class, ISaga
	{
		static readonly ILog _log = LogManager.GetLogger(typeof (NHibernateSagaStorage<TSaga>));

		readonly ISessionFactory _sessionFactory;

		public NHibernateSagaStorage(ISessionFactory sessionFactory)
		{
			_sessionFactory = sessionFactory;
		}

		public IEnumerable<Action<IConsumeContext<TMessage>>> GetSaga<TMessage>(IConsumeContext<TMessage> context, Guid sagaId,
		                                                                        InstanceHandlerSelector<TSaga, TMessage>
		                                                                        	selector, ISagaPolicy<TSaga, TMessage> policy)
			where TMessage : class
		{
			using (ISession session = _sessionFactory.OpenSession())
			using (ITransaction transaction = session.BeginTransaction())
			{
				var instance = session.Get<TSaga>(sagaId, LockMode.Write);
				if (instance == null)
				{
					if (policy.CanCreateInstance(context))
					{
						yield return x =>
							{
								if (_log.IsDebugEnabled)
									_log.DebugFormat("SAGA: {0} Creating New {1} for {2}", typeof (TSaga).ToFriendlyName(), sagaId,
										typeof (TMessage).ToFriendlyName());

								try
								{
									instance = policy.CreateInstance(x, sagaId);

									foreach (var callback in selector(instance, x))
									{
										callback(x);
									}

									if (!policy.CanRemoveInstance(instance))
										session.Save(instance);
								}
								catch (Exception ex)
								{
									var sex = new SagaException("Create Saga Instance Exception", typeof (TSaga), typeof (TMessage), sagaId, ex);
									if (_log.IsErrorEnabled)
										_log.Error(sex);

									throw sex;
								}
							};
					}
					else
					{
						if (_log.IsDebugEnabled)
							_log.DebugFormat("SAGA: {0} Ignoring Missing {1} for {2}", typeof (TSaga).ToFriendlyName(), sagaId,
								typeof (TMessage).ToFriendlyName());
					}
				}
				else
				{
					if (policy.CanUseExistingInstance(context))
					{
						yield return x =>
							{
								if (_log.IsDebugEnabled)
									_log.DebugFormat("SAGA: {0} Using Existing {1} for {2}", typeof (TSaga).ToFriendlyName(), sagaId,
										typeof (TMessage).ToFriendlyName());

								try
								{
									foreach (var callback in selector(instance, x))
									{
										callback(x);
									}

									if (policy.CanRemoveInstance(instance))
										session.Delete(instance);
								}
								catch (Exception ex)
								{
									var sex = new SagaException("Existing Saga Instance Exception", typeof (TSaga), typeof (TMessage), sagaId, ex);
									if (_log.IsErrorEnabled)
										_log.Error(sex);

									throw sex;
								}
							};
					}
					else
					{
						if (_log.IsDebugEnabled)
							_log.DebugFormat("SAGA: {0} Ignoring Existing {1} for {2}", typeof (TSaga).ToFriendlyName(), sagaId,
								typeof (TMessage).ToFriendlyName());
					}
				}

				transaction.Commit();
			}
		}
	}
}