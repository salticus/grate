﻿
using Oracle.TestInfrastructure;

namespace Oracle.Reported_issues.Non_ascii_characters_in_script;

[Collection(nameof(OracleGrateTestContext))]
// ReSharper disable once InconsistentNaming
public class ScriptsRunTable(OracleGrateTestContext testContext, ITestOutputHelper testOutput)
    : TestCommon.Generic.Reported_issues.Non_ascii_characters_in_script.ScriptsRunTable(testContext, testOutput);

