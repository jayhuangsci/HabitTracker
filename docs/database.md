# database schema

## aim

first version of database 

## principle

Structured Storage

## entity

Habit: e.g running,sleeping.Their attributes

HabitLog:particular log of some habit

e.g "i ran 5 km today",DB should shore date,quantity

## table design

HabitTable:

Name

ID

unit

HabitlogTable:


ID

habitID

Date

Quantity

comment

createdAt

## table interaction

One habit can have mutilple habitlog

But one habitlog should only belong to one habit

When deleted one Habit,delete all its habitlog too

## rules

log has to combile with one habit

Quantity can be numbers ,can not be negative

comments can be null

## example

Habit:Running,KM

Habitlog:2026-4-28,5,"ez"

## not trying to do

user table

login authentication table

