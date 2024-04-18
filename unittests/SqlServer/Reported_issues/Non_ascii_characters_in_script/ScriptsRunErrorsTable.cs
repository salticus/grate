﻿using SqlServer.TestInfrastructure;

namespace SqlServer.Reported_issues.Non_ascii_characters_in_script;

[Collection(nameof(SqlServerGrateTestContext))]
// ReSharper disable once InconsistentNaming
public class ScriptsRunErrorsTable(SqlServerGrateTestContext testContext, ITestOutputHelper testOutput)
    : TestCommon.Generic.Reported_issues.Non_ascii_characters_in_script.ScriptsRunErrorsTable(testContext, testOutput);

