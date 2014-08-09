﻿using System;
using System.Collections.Generic;
using NSubstitute;
using TestDriven.Framework;
using Xunit;
using Xunit.Abstractions;
using Xunit.Runner.TdNet;

public class ResultVisitorTests
{
    [Fact]
    public static void SignalsFinishedEventUponReceiptOfITestAssemblyFinished()
    {
        var listener = Substitute.For<ITestListener>();
        var visitor = new ResultVisitor(listener);
        var message = Substitute.For<ITestAssemblyFinished>();

        visitor.OnMessage(message);

        Assert.True(visitor.Finished.WaitOne(0));
    }

    public class RunState
    {
        [Fact]
        public static void DefaultRunStateIsNoTests()
        {
            var listener = Substitute.For<ITestListener>();
            var visitor = new ResultVisitor(listener);

            Assert.Equal(TestRunState.NoTests, visitor.TestRunState);
        }

        [Theory]
        [InlineData(TestRunState.NoTests)]
        [InlineData(TestRunState.Error)]
        [InlineData(TestRunState.Success)]
        public static void FailureSetsStateToFailed(TestRunState initialState)
        {
            var listener = Substitute.For<ITestListener>();
            var visitor = new ResultVisitor(listener) { TestRunState = initialState };

            visitor.OnMessage(Mocks.TestFailed(typeof(Object), "GetHashCode"));

            Assert.Equal(TestRunState.Failure, visitor.TestRunState);
        }

        [Fact]
        public static void Success_MovesToSuccess()
        {
            var listener = Substitute.For<ITestListener>();
            var visitor = new ResultVisitor(listener) { TestRunState = TestRunState.NoTests };

            visitor.OnMessage(Substitute.For<ITestPassed>());

            Assert.Equal(TestRunState.Success, visitor.TestRunState);
        }

        [Theory]
        [InlineData(TestRunState.Error)]
        [InlineData(TestRunState.Failure)]
        [InlineData(TestRunState.Success)]
        public static void Success_StaysInCurrentState(TestRunState initialState)
        {
            var listener = Substitute.For<ITestListener>();
            var visitor = new ResultVisitor(listener) { TestRunState = initialState };

            visitor.OnMessage(Substitute.For<ITestPassed>());

            Assert.Equal(initialState, visitor.TestRunState);
        }

        [Fact]
        public static void Skip_MovesToSuccess()
        {
            var listener = Substitute.For<ITestListener>();
            var visitor = new ResultVisitor(listener) { TestRunState = TestRunState.NoTests };

            visitor.OnMessage(Substitute.For<ITestSkipped>());

            Assert.Equal(TestRunState.Success, visitor.TestRunState);
        }

        [Theory]
        [InlineData(TestRunState.Error)]
        [InlineData(TestRunState.Failure)]
        [InlineData(TestRunState.Success)]
        public static void Skip_StaysInCurrentState(TestRunState initialState)
        {
            var listener = Substitute.For<ITestListener>();
            var visitor = new ResultVisitor(listener) { TestRunState = initialState };

            visitor.OnMessage(Substitute.For<ITestSkipped>());

            Assert.Equal(initialState, visitor.TestRunState);
        }
    }

    public class FailureInformation
    {
        static TMessageType MakeFailureInformationSubstitute<TMessageType>()
            where TMessageType : class, IFailureInformation
        {
            var result = Substitute.For<TMessageType>();
            result.ExceptionTypes.Returns(new[] { "ExceptionType" });
            result.Messages.Returns(new[] { "This is my message \t\r\n" });
            result.StackTraces.Returns(new[] { "Line 1\r\nLine 2\r\nLine 3" });
            return result;
        }

        public static IEnumerable<object[]> Messages
        {
            get
            {
                yield return new object[] { MakeFailureInformationSubstitute<IErrorMessage>(), "Fatal Error" };

                var assemblyCleanupFailure = MakeFailureInformationSubstitute<ITestAssemblyCleanupFailure>();
                var testAssembly = Mocks.TestAssembly(@"C:\Foo\bar.dll");
                assemblyCleanupFailure.TestAssembly.Returns(testAssembly);
                yield return new object[] { assemblyCleanupFailure, @"Test Assembly Cleanup Failure (C:\Foo\bar.dll)" };

                var collectionCleanupFailure = MakeFailureInformationSubstitute<ITestCollectionCleanupFailure>();
                var testCollection = Mocks.TestCollection(displayName: "FooBar");
                collectionCleanupFailure.TestCollection.Returns(testCollection);
                yield return new object[] { collectionCleanupFailure, "Test Collection Cleanup Failure (FooBar)" };

                var classCleanupFailure = MakeFailureInformationSubstitute<ITestClassCleanupFailure>();
                var testClass = Mocks.TestClass("MyType");
                classCleanupFailure.TestClass.Returns(testClass);
                yield return new object[] { classCleanupFailure, "Test Class Cleanup Failure (MyType)" };

                var methodCleanupFailure = MakeFailureInformationSubstitute<ITestMethodCleanupFailure>();
                var testMethod = Mocks.TestMethod(methodName: "MyMethod");
                methodCleanupFailure.TestMethod.Returns(testMethod);
                yield return new object[] { methodCleanupFailure, "Test Method Cleanup Failure (MyMethod)" };

                var testCaseCleanupFailure = MakeFailureInformationSubstitute<ITestCaseCleanupFailure>();
                var testCase = Mocks.TestCase(typeof(Object), "ToString", displayName: "MyTestCase");
                testCaseCleanupFailure.TestCase.Returns(testCase);
                yield return new object[] { testCaseCleanupFailure, "Test Case Cleanup Failure (MyTestCase)" };

                var testCleanupFailure = MakeFailureInformationSubstitute<ITestCleanupFailure>();
                testCleanupFailure.TestDisplayName.Returns("MyTest");
                yield return new object[] { testCleanupFailure, "Test Cleanup Failure (MyTest)" };
            }
        }

        [Theory]
        [MemberData("Messages")]
        public static void LogsTestFailure(IMessageSinkMessage message, string messageType)
        {
            var listener = Substitute.For<ITestListener>();
            using (var visitor = new ResultVisitor(listener) { TestRunState = TestRunState.NoTests })
            {
                visitor.OnMessage(message);

                Assert.Equal(TestRunState.Failure, visitor.TestRunState);
                var testResult = listener.Captured(x => x.TestFinished(null)).Arg<TestResult>();
                Assert.Equal(String.Format("*** {0} ***", messageType), testResult.Name);
                Assert.Equal(TestState.Failed, testResult.State);
                Assert.Equal(1, testResult.TotalTests);
                Assert.Equal("ExceptionType : This is my message \t\r\n", testResult.Message);
                Assert.Equal("Line 1\r\nLine 2\r\nLine 3", testResult.StackTrace);
            }
        }
    }

    public class MessageConversion
    {
        [Fact]
        public static void ConvertsITestPassed()
        {
            TestResult testResult = null;
            var listener = Substitute.For<ITestListener>();
            listener.WhenAny(l => l.TestFinished(null))
                    .Do<TestResult>(result => testResult = result);
            var visitor = new ResultVisitor(listener);
            var message = Mocks.TestPassed(typeof(string), "Contains", "Display Name", executionTime: 123.45M);

            visitor.OnMessage(message);

            Assert.NotNull(testResult);
            Assert.Same(typeof(string), testResult.FixtureType);
            Assert.Equal("Contains", testResult.Method.Name);
            Assert.Equal("Display Name", testResult.Name);
            Assert.Equal(TestState.Passed, testResult.State);
            Assert.Equal(123.45, testResult.TimeSpan.TotalMilliseconds);
            Assert.Equal(1, testResult.TotalTests);
        }

        [Fact]
        public static void ConvertsITestFailed()
        {
            Exception ex;

            try
            {
                throw new Exception();
            }
            catch (Exception e)
            {
                ex = e;
            }

            TestResult testResult = null;
            var listener = Substitute.For<ITestListener>();
            listener.WhenAny(l => l.TestFinished(null))
                    .Do<TestResult>(result => testResult = result);
            var visitor = new ResultVisitor(listener);
            var message = Mocks.TestFailed(typeof(string), "Contains", "Display Name", executionTime: 123.45M, ex: ex);

            visitor.OnMessage(message);

            Assert.NotNull(testResult);
            Assert.Same(typeof(string), testResult.FixtureType);
            Assert.Equal("Contains", testResult.Method.Name);
            Assert.Equal("Display Name", testResult.Name);
            Assert.Equal(TestState.Failed, testResult.State);
            Assert.Equal(123.45, testResult.TimeSpan.TotalMilliseconds);
            Assert.Equal(1, testResult.TotalTests);
            Assert.Equal("System.Exception : " + ex.Message, testResult.Message);
            Assert.Equal(ex.StackTrace, testResult.StackTrace);
        }

        [Fact]
        public static void ConvertsITestSkipped()
        {
            TestResult testResult = null;
            var listener = Substitute.For<ITestListener>();
            listener.WhenAny(l => l.TestFinished(null))
                    .Do<TestResult>(result => testResult = result);
            var visitor = new ResultVisitor(listener);
            var message = Mocks.TestSkipped(typeof(string), "Contains", "Display Name", executionTime: 123.45M, skipReason: "I forgot how to run");

            visitor.OnMessage(message);

            Assert.NotNull(testResult);
            Assert.Same(typeof(string), testResult.FixtureType);
            Assert.Equal("Contains", testResult.Method.Name);
            Assert.Equal("Display Name", testResult.Name);
            Assert.Equal(TestState.Ignored, testResult.State);
            Assert.Equal(123.45, testResult.TimeSpan.TotalMilliseconds);
            Assert.Equal(1, testResult.TotalTests);
            Assert.Equal("I forgot how to run", testResult.Message);
        }
    }
}