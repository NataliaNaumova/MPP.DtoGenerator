﻿using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Microsoft.CodeAnalysis.CSharp;

namespace DTOGenerator
{
    public class DTOGenerator
    {
        private readonly int threadPoolLimit;
        private readonly string dtoNamespace;
        private int workingThreadsCount;
        private Queue<object[]> localTaskQueue = new Queue<object[]>();

        public DTOGenerator(int threadPoolLimit, string dtoNamespace)
        {
            this.dtoNamespace = dtoNamespace;
            this.threadPoolLimit = threadPoolLimit;
            workingThreadsCount = 0;
        }

        public List<DTODescription> GenerateCode(List<ClassDescription> classesList)
        {
            List<DTODescription> classUnits = new List<DTODescription>();

            using (CountdownEvent countDownEvent = new CountdownEvent(classesList.Count))
            using (ManualResetEvent manualResetEvent = new ManualResetEvent(true))
            {
                foreach (ClassDescription classDescription in classesList)
                {
                    DTODescription dtoDescription = new DTODescription(classDescription.ClassName);
                    classUnits.Add(dtoDescription);
                    manualResetEvent.WaitOne();
                    
                    ThreadPool.QueueUserWorkItem(delegate (object state)
                    {
                        try
                        {
                            Interlocked.Increment(ref workingThreadsCount);
                            if (Volatile.Read(ref workingThreadsCount) == threadPoolLimit)
                            {
                                manualResetEvent.Set();
                            }
                            GenerateClassCode(state);
                        }
                        finally
                        {
                            countDownEvent.Signal();
                            Interlocked.Decrement(ref workingThreadsCount);
                            if (Volatile.Read(ref workingThreadsCount) < threadPoolLimit)
                            {
                                manualResetEvent.Reset();
                            }
                        }
                    },
                    new object[] { classDescription, dtoDescription });
                }

                countDownEvent.Wait();
            }

            return classUnits;
        } 

        private void GenerateClassCode(Object state)
        {
            object[] array = state as object[];
            ClassDescription classDescription = (ClassDescription) array[0];
            DTODescription dtoDescription = (DTODescription)array[1];

            SyntaxList<MemberDeclarationSyntax> memberDeclarationSyntaxesList = new SyntaxList<MemberDeclarationSyntax>();

            foreach (PropertyDescription propertyDescription in classDescription.PropertyDescriptions)
            {
                memberDeclarationSyntaxesList = memberDeclarationSyntaxesList.Add(GeneratePropertyCode(propertyDescription));
            }

            CompilationUnitSyntax compilationUnit = CompilationUnit()
            .WithUsings(
                SingletonList<UsingDirectiveSyntax>(
                    UsingDirective(
                        IdentifierName("System"))))
            .WithMembers(
                SingletonList<MemberDeclarationSyntax>(
                    NamespaceDeclaration(
                        IdentifierName(dtoNamespace))
                    .WithMembers(
                        SingletonList<MemberDeclarationSyntax>(
                            ClassDeclaration(classDescription.ClassName)
                            .WithMembers(memberDeclarationSyntaxesList)))))
            .NormalizeWhitespace();

            dtoDescription.SyntaxTree = compilationUnit.SyntaxTree;
        }

        private MemberDeclarationSyntax GeneratePropertyCode(PropertyDescription propertyDescription)
        {
            MemberDeclarationSyntax propertyCode = PropertyDeclaration(
                                                        IdentifierName(propertyDescription.Type.FullName),
                                                        Identifier(propertyDescription.Name))
                                                    .WithModifiers(
                                                        TokenList(
                                                            Token(SyntaxKind.PublicKeyword)))
                                                    .WithAccessorList(
                                                        AccessorList(
                                                            List<AccessorDeclarationSyntax>(
                                                                new AccessorDeclarationSyntax[]{
                                                                    AccessorDeclaration(
                                                                        SyntaxKind.GetAccessorDeclaration)
                                                                    .WithSemicolonToken(
                                                                        Token(SyntaxKind.SemicolonToken)),
                                                                    AccessorDeclaration(
                                                                        SyntaxKind.SetAccessorDeclaration)
                                                                    .WithModifiers(
                                                                        TokenList(
                                                                            Token(SyntaxKind.PrivateKeyword)))
                                                                    .WithSemicolonToken(
                                                                        Token(SyntaxKind.SemicolonToken))})));
            return propertyCode;
        }
    }
}
