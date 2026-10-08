# Проверки Кицу · .NET 10

Проверки выполняются на Windows с установленным [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). SDK нужен разработчикам; скачанный автономный `Kitsu.exe` уже содержит среду выполнения. Для первого восстановления пакетов требуется интернет.

Откройте Windows PowerShell в папке проекта и запустите:

```powershell
.\qa\run-tests.ps1
```

Скрипт собирает отдельные консольные программы из настоящих исходников с 18 встроенными наборами изображений. `artifacts\tests\sprites` содержит проверку кадров и физики, а `artifacts\tests\behavior` — проверку игровых состояний. Проверочные программы запускаются через .NET 10; основное приложение `artifacts\win-x64\Kitsu.exe` не перезаписывается.

Проверяются фазы анимаций, альфа-прозрачность, движение и успокоение всех трёх игрушек, включая монитор с отрицательными координатами. `ToyPlayTest.cs` проверяет подбор, переноску, тряску, перекатывание, бросок, укладывание, жевание, вставание и повторную погоню. Также проверяются удержание того же объекта, прерывание командой или лаской, отпускание ровно один раз и сохранение игрушки после завершения игры.

## Визуальная проверка игрушек

`ToyDiagnostics.cs` создаёт листы кадров переноски, броска и переходов к жеванию. Соберите и запустите её отдельно:

```powershell
.\build.ps1 -OutputPath .\artifacts\tests\diagnostics\toy-diagnostics.exe -Console -TestSource .\qa\ToyDiagnostics.cs -MainType KitsuDesktop.ToyDiagnostics
dotnet .\artifacts\tests\diagnostics\toy-diagnostics.dll
```

PNG сохраняются рядом с этой проверочной сборкой, в `artifacts\tests\diagnostics`.

## Жизненный цикл окон

`ToyWindowLifecycle.cs` проверяет создание, скрытие, повторное показание и освобождение прозрачных окон игрушек: 120 окон и 600 циклов удержания/отпускания. Ей нужен обычный сеанс рабочего стола Windows.

```powershell
.\build.ps1 -OutputPath .\artifacts\tests\windows\toy-windows.exe -Console -TestSource .\qa\ToyWindowLifecycle.cs -MainType KitsuDesktop.ToyWindowLifecycle
dotnet .\artifacts\tests\windows\toy-windows.dll
```

## Проверка готового приложения

После обычной сборки:

```powershell
.\artifacts\win-x64\Kitsu.exe --smoke-test
```

Кицу показывает прозрачное окно на три секунды с отключённой игрой с иконками. Результат записывается в `smoke-test.txt` рядом с EXE; если папка недоступна для записи — в `%LocalAppData%\Kitsu\smoke-test.txt`.

Журнал ошибок хранится в `%LocalAppData%\Kitsu\kitsu-error.txt`, с резервным путём `%TEMP%\Kitsu\kitsu-error.txt`. CLI ошибки также выводятся в консоль и возвращают код 1 без диалогового окна; при доступной папке приложения сохраняется совместимый файл `kitsu-error.txt` рядом с EXE.
