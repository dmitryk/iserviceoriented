﻿using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using System.Threading;
using IServiceOriented.ServiceBus.Threading;
using System.Collections.ObjectModel;

using IServiceOriented.ServiceBus.Delivery;
using IServiceOriented.ServiceBus.Listeners;
using IServiceOriented.ServiceBus.Dispatchers;
using System.ServiceModel;
using System.ServiceModel.Channels;

namespace IServiceOriented.ServiceBus.UnitTests
{
    
    public class ServiceBusTest
    {
        public ServiceBusTest(ServiceBusRuntime runtime)
        {
            serviceBusRuntime = runtime;
        }

        ServiceBusRuntime serviceBusRuntime;

        public void AssertEqual(SubscriptionEndpoint endpoint1, SubscriptionEndpoint endpoint2)
        {
            Assert.AreEqual(endpoint1.Id, endpoint2.Id);
            Assert.AreEqual(endpoint1.Name, endpoint2.Name);
            Assert.AreEqual(endpoint1.ConfigurationName, endpoint2.ConfigurationName);
            Assert.AreEqual(endpoint1.ContractType, endpoint2.ContractType);
            Assert.AreEqual(endpoint1.Address, endpoint2.Address);
            
            Assert.IsInstanceOfType(endpoint1.Dispatcher.GetType(), endpoint2.Dispatcher);
        }

        public void AssertEqual(ListenerEndpoint endpoint1, ListenerEndpoint endpoint2)
        {
            Assert.AreEqual(endpoint1.Id, endpoint2.Id);
            Assert.AreEqual(endpoint1.Name, endpoint2.Name);
            Assert.AreEqual(endpoint1.ConfigurationName, endpoint2.ConfigurationName);
            Assert.AreEqual(endpoint1.ContractType, endpoint2.ContractType);
            Assert.AreEqual(endpoint1.Address, endpoint2.Address);
            Assert.IsInstanceOfType(endpoint1.Listener.GetType(), endpoint2.Listener); // todo: should we require .Equals?
        }

        public void VerifyQueuesEmpty()
        {
            QueuedDeliveryCore core = serviceBusRuntime.ServiceLocator.GetInstance<QueuedDeliveryCore>();
            Assert.IsNull(core.RetryQueue.Peek(TimeSpan.FromMilliseconds(500)));
            Assert.IsNull(core.FailureQueue.Peek(TimeSpan.FromMilliseconds(500)));
            Assert.IsNull(core.MessageDeliveryQueue.Peek(TimeSpan.FromMilliseconds(500)));
        }

        public void OnlyRetryOnce()
        {
            serviceBusRuntime.ServiceLocator.GetInstance<QueuedDeliveryCore>().ExponentialBackOff = false;
            serviceBusRuntime.ServiceLocator.GetInstance<QueuedDeliveryCore>().RetryDelay = 100;
            serviceBusRuntime.MaxRetries = 1;
        }

        public void AddTestListener()
        {
            serviceBusRuntime.AddListener(new ListenerEndpoint(Guid.NewGuid(), "test", "NamedPipeListener", "net.pipe://localhost/servicebus/testlistener", typeof(IContract), new WcfServiceHostListener()));
        }

        public void AddTestSubscription(ContractImplementation ci, MessageFilter messageFilter)
        {
            AddTestSubscription(ci, messageFilter, null);
        }

        public void AddTestSubscription(ContractImplementation ci, MessageFilter messageFilter, DateTime? expiration)
        {
            serviceBusRuntime.Subscribe(new SubscriptionEndpoint(Guid.NewGuid(), "subscription", "", "", typeof(IContract), new MethodDispatcher(ci), messageFilter, false, expiration));
        }

        public void StartAndStop(Action inner)
        {
            serviceBusRuntime.Start();
            
            inner();
            
            serviceBusRuntime.Stop();            
        }

        public void WaitForDeliveriesAndFailures(int deliveryCount, int failureCount, TimeSpan timeout, Action inner)
        {
            waitForDeliveries(deliveryCount, failureCount, true, timeout, inner);
        }

        public void WaitForDeliveries(int deliveryCount, TimeSpan timeout, Action inner)
        {
            waitForDeliveries(deliveryCount, 0, false, timeout, inner);
        }

        void waitForDeliveries(int deliveryCount, int failureCount, bool includeFailures, TimeSpan timeout, Action inner)
        {
            using (CountdownLatch latch = new CountdownLatch(deliveryCount))
            {
                using (CountdownLatch failLatch = new CountdownLatch(failureCount))
                {

                    EventHandler<MessageDeliveryEventArgs> delivered = (o, mdea) => { Console.WriteLine("s");  latch.Tick(); };
                    EventHandler<MessageDeliveryFailedEventArgs> deliveryFailed = (o, mdfa) => { Console.WriteLine("f"); failLatch.Tick(); };

                    serviceBusRuntime.MessageDelivered += delivered;
                    if (includeFailures) serviceBusRuntime.MessageDeliveryFailed += deliveryFailed;

                    try
                    {
                        StartAndStop(() =>
                        {
                            inner();

                            if (!WaitHandle.WaitAll(new WaitHandle[] { latch.Handle, failLatch.Handle } , timeout))
                            {
                                throw new TimeoutException("timeout expired");
                            }
                        });
                    }
                    finally
                    {
                        serviceBusRuntime.MessageDelivered -= delivered;
                        if (includeFailures) serviceBusRuntime.MessageDeliveryFailed -= deliveryFailed;
                    }
                }

                
            }
        }

    }

    [TestFixture]
    public class TestServiceBusRuntime
    {
        public TestServiceBusRuntime()
        {            
            
        }

        
        
        [Test]
        public void MessageFilter_Properly_Excludes_Messages()
        {
            using (var serviceBusRuntime = Create.MsmqRuntime<IContract>())
            {
                ServiceBusTest tester = new ServiceBusTest(serviceBusRuntime);
   
                string message = "Publish this message";

                ContractImplementation ci = new ContractImplementation();
                ci.SetFailCount(1);

                tester.OnlyRetryOnce();
                
                tester.AddTestListener();
                tester.AddTestSubscription(ci, new BooleanMessageFilter(false));


                try
                {
                    tester.WaitForDeliveries(2, TimeSpan.FromSeconds(5), () =>
                    {
                        serviceBusRuntime.PublishOneWay(new PublishRequest(typeof(IContract), "PublishThis", message));
                    });
                }
                catch (TimeoutException) // should timeout while waiting
                {
                }
            
                Assert.AreEqual(0, ci.PublishedCount);

                tester.VerifyQueuesEmpty(); 
            }
        }

        [Test]
        public void MessageFilter_Properly_Includes_Messages()
        {
            using (var serviceBusRuntime = Create.MsmqRuntime<IContract>())
            {
                ServiceBusTest tester = new ServiceBusTest(serviceBusRuntime);
                tester.OnlyRetryOnce();

                ContractImplementation ci = new ContractImplementation();
                ci.SetFailCount(0);

                tester.AddTestListener();
                tester.AddTestSubscription(ci, new BooleanMessageFilter(true));

                string message = "Publish this message";
                
                tester.WaitForDeliveries(2, TimeSpan.FromMinutes(1), ()=>
                {
                    serviceBusRuntime.PublishOneWay(new PublishRequest(typeof(IContract), "PublishThis", message));
                });
                
                Assert.AreEqual(1, ci.PublishedCount);
                Assert.AreEqual(message, ci.PublishedMessages[0]);

                tester.VerifyQueuesEmpty(); 
            }
        }

        [Test]
        public void Dispatcher_Receives_Messages()
        {
            using (var serviceBusRuntime = Create.MsmqRuntime<IContract>())
            {
                ServiceBusTest tester = new ServiceBusTest(serviceBusRuntime);
                
                string message = "Publish this message";
                ContractImplementation ci = new ContractImplementation();
                ci.SetFailCount(0);

                tester.AddTestListener();
                tester.AddTestSubscription(ci, new PassThroughMessageFilter());

                tester.WaitForDeliveries(2, TimeSpan.FromSeconds(50), () =>
                {
                    serviceBusRuntime.PublishOneWay(new PublishRequest(typeof(IContract), "PublishThis", message));
                });
            
                Assert.AreEqual(1, ci.PublishedCount);
                Assert.AreEqual(message, ci.PublishedMessages[0]);

                tester.VerifyQueuesEmpty(); 
            }
        }

        [Test]
        public void Can_Deliver_Many_Messages()
        {
            using (var serviceBusRuntime = Create.MsmqRuntime<IContract>())
            {
                ServiceBusTest tester = new ServiceBusTest(serviceBusRuntime);
                
                ContractImplementation ci = new ContractImplementation();
                ci.SetFailCount(0);

                tester.AddTestListener();
                tester.AddTestSubscription(ci, new PassThroughMessageFilter());                

                int messageCount = 1000;
                
                
                for (int i = 0; i < messageCount; i++)
                {
                    string message = i.ToString();
                    serviceBusRuntime.PublishOneWay(new PublishRequest(typeof(IContract), "PublishThis", message));
                }

                DateTime start = DateTime.Now;

                tester.WaitForDeliveries(messageCount*2, TimeSpan.FromMinutes(1), () =>
                {
                                     
                });
            
                
                bool[] results = new bool[messageCount];
                
                DateTime end = DateTime.Now;

                System.Diagnostics.Trace.TraceInformation("Time to deliver messages "+messageCount+" = "+(end - start)); 
                
                Assert.AreEqual(messageCount, ci.PublishedCount);
                
                for(int i = 0; i < ci.PublishedCount; i++)
                {
                    int j = Convert.ToInt32(ci.PublishedMessages[i]);
                    results[j] = true;                    
                }

                for (int i = 0; i < messageCount; i++)
                {
                    Assert.IsTrue(results[i]);
                }

                tester.VerifyQueuesEmpty(); 
            }
        }

        [Test]
        public void Can_Deliver_Many_Messages_With_Failures()
        {
            using (var serviceBusRuntime = Create.MsmqRuntime<IContract>())
            {
                ServiceBusTest tester = new ServiceBusTest(serviceBusRuntime);                
                tester.OnlyRetryOnce();

                ContractImplementation ci = new ContractImplementation();
                ci.SetFailCount(0);
                ci.SetFailInterval(10);

                QueuedDeliveryCore qc = serviceBusRuntime.ServiceLocator.GetInstance<QueuedDeliveryCore>();
                qc.RetryDelay = 1;
                
                tester.AddTestListener();
                tester.AddTestSubscription(ci, new PassThroughMessageFilter());

                int messageCount = 1000;

                DateTime start = DateTime.Now;

            
                tester.WaitForDeliveries(messageCount*2, TimeSpan.FromMinutes(1), () =>
                {
                    for (int i = 0; i < messageCount; i++)
                    {
                        string message = i.ToString();
                        serviceBusRuntime.PublishOneWay(new PublishRequest(typeof(IContract), "PublishThis", message));
                    }
                });
                            

                bool[] results = new bool[messageCount];
                // Wait for delivery
                DateTime end = DateTime.Now;
                System.Diagnostics.Trace.TraceInformation("Time to deliver " + messageCount + " = " + (end - start));
                
                for (int i = 0; i < ci.PublishedCount; i++)
                {
                    results[Convert.ToInt32(ci.PublishedMessages[i])] = true;
                }

                for (int i = 0; i < messageCount; i++)
                {
                    Assert.IsTrue(results[i]);
                }

                Assert.AreEqual(messageCount, ci.PublishedCount);
                tester.VerifyQueuesEmpty(); 
            }
        
        }

        [Test]
        public void Retry_Queue_Receives_Initial_Failures()
        {
            using (var serviceBusRuntime = Create.MsmqRuntime<IContract>())
            {
                ServiceBusTest tester = new ServiceBusTest(serviceBusRuntime);

                string message = "Publish this message";
                ContractImplementation ci = new ContractImplementation();
                ci.SetFailCount(1);
                
                tester.OnlyRetryOnce();
                tester.AddTestListener();

                tester.AddTestSubscription(ci, new PassThroughMessageFilter());

                bool failFirst = false;
                bool deliverSecond = false;

                tester.StartAndStop(() =>
                {
                    CountdownLatch latch = new CountdownLatch(2+1);

                    serviceBusRuntime.MessageDelivered += (o, mdea) =>
                    {
                        int tick; if ((tick = latch.Tick()) == 0) deliverSecond = true; Console.WriteLine("Tick deliver " + tick);
                    };
                    serviceBusRuntime.MessageDeliveryFailed += (o, mdfea) =>
                    {
                        int tick; if ((tick = latch.Tick()) == 1) failFirst = true; Console.WriteLine("Tick fail " + tick);
                    };

                    serviceBusRuntime.PublishOneWay(new PublishRequest(typeof(IContract), "PublishThis", message));

                    // Wait for delivery
                    latch.Handle.WaitOne(TimeSpan.FromMinutes(1), false); // give it a minute

                });

                Assert.AreEqual(true, failFirst);
                Assert.AreEqual(true, deliverSecond);
                
                Assert.AreEqual(1, ci.PublishedCount);
                Assert.AreEqual(message, ci.PublishedMessages[0]);

                
                tester.VerifyQueuesEmpty(); 
            }
        }

        [Test]
        public void Expired_Subscriptions_Do_Not_Get_Messages()
        {            
            using (var serviceBusRuntime = new ServiceBusRuntime())
            {
                ServiceBusTest tester = new ServiceBusTest(serviceBusRuntime);

                string message = "Publish this message";
                ContractImplementation ci = new ContractImplementation();
                
                tester.AddTestSubscription(ci, new PassThroughMessageFilter(), DateTime.MinValue);


                try
                {
                    tester.WaitForDeliveries(2, TimeSpan.FromSeconds(1), () =>
                    {
                        serviceBusRuntime.PublishOneWay(new PublishRequest(typeof(IContract), "PublishThis", message));
                    });
                    Assert.Fail("Message should not have been delivered to an expired subscription.");
                }
                catch (TimeoutException)
                {

                }
            }
        }

        [Test]
        public void Not_Yet_Expired_Subscriptions_Get_Messages()
        {
            using (var serviceBusRuntime = new ServiceBusRuntime())
            {
                ServiceBusTest tester = new ServiceBusTest(serviceBusRuntime);

                string message = "Publish this message";
                ContractImplementation ci = new ContractImplementation();

                tester.AddTestSubscription(ci, new PassThroughMessageFilter(), DateTime.MaxValue);


                try
                {
                    tester.WaitForDeliveries(2, TimeSpan.FromSeconds(1), () =>
                    {
                        serviceBusRuntime.PublishOneWay(new PublishRequest(typeof(IContract), "PublishThis", message));
                    });                    
                }
                catch (TimeoutException)
                {
                    Assert.Fail("Message should have been delivered to not yet expired subscription.");
                }
            }
        }


        [Test]
        public void Failure_Queue_Receives_Messages_When_Retries_Maxed()
        {
            using (var serviceBusRuntime = Create.MsmqRuntime<IContract>())
            {
                ServiceBusTest tester = new ServiceBusTest(serviceBusRuntime);                
                tester.OnlyRetryOnce();

                string message = "Publish this message";
                ContractImplementation ci = new ContractImplementation();
                ci.SetFailCount(3);

                tester.AddTestListener();
                tester.AddTestSubscription(ci, new PassThroughMessageFilter());

                tester.WaitForDeliveriesAndFailures(1, 3, TimeSpan.FromSeconds(10), () =>
                {
                    serviceBusRuntime.PublishOneWay(new PublishRequest(typeof(IContract), "PublishThis", message));
                });                   
        
                Assert.AreEqual(0, ci.PublishedCount);

                MessageDelivery delivery = serviceBusRuntime.ServiceLocator.GetInstance<QueuedDeliveryCore>().FailureQueue.Dequeue(TimeSpan.FromSeconds(1));
                Assert.IsNotNull(delivery);

                tester.VerifyQueuesEmpty(); 
                Assert.AreEqual(3, ((IEnumerable<string>)delivery.Context[MessageDelivery.Exceptions]).Count());           
            }
        }   
    }
}
