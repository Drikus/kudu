var curWorkingDir = ko.observable("");
window.KuduExec = { workingDir: curWorkingDir };

$(function () {
    // call make console after this first command so the current working directory is set.
    var lastLine = "";
    var lastUserInput = null;
    var kuduExecConsole = $('<div class="console">');
    var curReportFun;
    var controller = kuduExecConsole.console({
        continuedPrompt: true,
        promptLabel: function() {
            return getJSONValue(lastLine);
        },
        commandValidate: function() {
            return true;
        },
        commandHandle: function (line, reportFn) {
            curReportFun = reportFn;
            if (line.trim().toUpperCase() === "CLS") {
                controller.reset();
                controller.message("", "jquery-console-message-value");
            } else {
                lastUserInput = line + "\n";
                lastLine.Output ? lastLine.Output += lastUserInput : lastLine.Error += lastUserInput;
                _sendCommand(line);
                controller.enableInput();
            }
        },
        cancelHandle: function() {
            _sendMessage({ "break": true });
            curReportFun("Command canceled by user.", "jquery-console-message-error");
        },
        completeHandle: function(line) {
            return;
        },
        cols: 3,
        autofocus: true,
        animateScroll: true,
        promptHistory: true,
        welcomeMessage: "Kudu Remote Execution Console\r\nType 'exit' to reset this console.\r\n\r\n"
    });
    $('#KuduExecConsole').append(kuduExecConsole);

    var connection = $.connection('/commandstream');

    connection.start({
        waitForPageLoad: true,
        transport: "auto"
    });

    connection.received(function (data) {
        var array;
        var isError = false;
        if (data.Output) {
            array = data.Output.split('\n');
        }
        else if (data.Error) {
            array = data.Error.split('\n');
            isError = true;
        }
        
        if (array) {
            for (var i = 0; i < array.length; i++) {
                var line = array[i];
                if (i != array.length - 1)
                    line += "\n";
                var display = new Object;
                if (isError) {
                    display.Error = line;
                }
                else {
                    display.Output = line;
                }
                DisplayAndUpdate(display);
            }
        }
    });
    
    function _sendCommand(input) {
        _sendMessage(input);
    }

    function _sendMessage(input) {
        connection.send(input);
    }
    
    function endsWith(str, suffix) {
        return str.indexOf(suffix, str.length - suffix.length) !== -1;
    }
    
    function getJSONValue(input) {
        return (input.Output || input.Error || "").toString();
    }
    
    function DisplayAndUpdate(data) {
        var prompt = getJSONValue(data);
        if (lastUserInput && prompt == lastUserInput)
            return;
        if (windowsPathValidation(prompt.replace("\n", "").replace(">", ""))) {
            if (!window.KuduExec.appRoot) {
                window.KuduExec.appRoot = prompt.replace("\n", "").replace(">", "");
            } else {
                curWorkingDir(prompt.replace("\n", "").replace(">", ""));
            }
            
        }
        
        //if the data has the same class as the last ".jquery-console-message"
        //then just append it to the last one, if not, create a new div.
        var lastLinestr = getJSONValue(lastLine);
        var lastConsoleMessage = $(".jquery-console-message").last();
        lastConsoleMessage.text(lastConsoleMessage.text() + lastLinestr);
        $(".jquery-console-inner").append($(".jquery-console-prompt-box"));
        $(".jquery-console-cursor").parent().prev(".jquery-console-prompt-label").text(prompt);
        controller.promptText("");
        controller.ringn = 0;
        controller.scrollToBottom();

        //Now create the div for the new line that will be printed the next time with the correct class
        if (data.Error) {
            if (!lastConsoleMessage.hasClass("jquery-console-message-error")) {
                controller.message("", "jquery-console-message-error");
            }
        }
        else if (!lastConsoleMessage.hasClass("jquery-console-message-value") || endsWith(lastLinestr, "\n")){
            controller.message("", "jquery-console-message-value");
        }

        //save last line for next time.
        lastLine = data;
    }

    function windowsPathValidation(contwinpath) {
        if (contwinpath == "")
            return false;
        if ((contwinpath.charAt(0) != "\\" || contwinpath.charAt(1) != "\\") || (contwinpath.charAt(0) != "/" || contwinpath.charAt(1) != "/")) {
            if (!contwinpath.charAt(0).match(/^[a-zA-Z]/)) {
                return false;
            }
            if (!contwinpath.charAt(1).match(/^[:]/) || !contwinpath.charAt(2).match(/^[\/\\]/)) {
                return false;
            }
        }
        return true;
    }
});
