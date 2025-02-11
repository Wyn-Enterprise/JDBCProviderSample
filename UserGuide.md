# Introduction

This data provider can connect to database via JDBC driver.

In Wyn, this data provider is treated as a Native data provider, which means that you can only use it in the following scenarios:
* Native dataset designer
* CustomSQLTable in direct/cache dataset designer

# JDBC drivers

JDBC drivers are NOT included in this data provider itself. You must specify the JDBC driver path to initiate the connection.
See connection string pattern section for more details.

# Connection string pattern

`DriverClass=???;DriverClass=???;JdbcUrl=???`

Sample:
```
	DriverPath = "mysql-connector-j-8.2.0.jar";
	DriverClass = "com.mysql.cj.jdbc.Driver";
	JdbcUrl = "jdbc:mysql://10.32.5.241:3306/stg_GEF11977?user=root&password=unknown";
```

The connection string is a combination of three parts:
* DriverPath: The relative path to the JDBC driver jar file. The path is relative to the location of the primary library of this data provider. In docker environment, you can mount the jar file to the container and specify the path here.
* DriverClass: The class name of the JDBC driver. This is required to load the driver.
* JdbcUrl: The JDBC connection string. This is the URL that the driver will use to connect to the database.

Like any other built-in data providers, you can use `@{user_context_name}` and/or `#{organization_context_name}` in the connection string to refer to the context variables.

# Command text pattern

The command text obeys the SQL syntax of the database you are connecting to.

# Limitations

## Data type mapping

This data provider uses gRpc and protobuf to communicate with the Java runtime. Data of some unconventional data types may not be supported.

If your database table contains columns of such data types, you may need to write your command text carefully to avoid these columns.

## RowLimitOption

Sometime Wyn intends to retrieve the result schema of a given command. However, this provider does not know what the underlying database is, so it is impossible to attach a "TOPN" or "LIMIT" clause to the command text.

As a workaround, the provider will try to append a "LIMIT ?" clause to the command text if needed. This is not guaranteed to work. If the try fails, the provider will execute the raw command and take only the rows of specified limit.

This known limitation may impact the performance when:
* Creating CustomSQLTable in direct/cache dataset designer
* Entering dataset designer to edit existing direct/cache dataset using this provider

From this perspective, it is recommended to use this provider in the scenarios where the command will not return a large number of rows.

## Data source preview

Like built-in native data providers, this provider does not support data source preview.

## Platform support

This data provider works with Wyn service deployed on Windows(x64) and Linux(x64).

# Disclaimer

* The source code of this data provider is provided as-is. You can modify it to fit your needs. Also, you can redistribute it without any restrictions.
* This data provider is a demo provider published by Wyn team for the purpose of demonstrating how to create a custom data provider. It is not guaranteed to work in all scenarios. If you have any questions, please post them in the Wyn forum.
