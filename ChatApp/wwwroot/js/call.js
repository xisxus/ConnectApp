// wwwroot/js/call.js
"use strict";

const callHubConnection = new signalR.HubConnectionBuilder()
    .withUrl("/callHub")
    .withAutomaticReconnect()
    .build();

let pc = null;
let localStream = null;
let remoteStream = null;
let currentCallPartner = null;
let currentCallType = null;
let pendingOffer = null;
let isCallActive = false;

const rtcConfig = {
    iceServers: [
        { urls: "stun:stun.l.google.com:19302" },
        { urls: "stun:stun1.l.google.com:19302" }
    ]
};

function getSelectedChatTargetUsername() {
    if (window.selectedTarget && window.selectedTarget.type === 'user') {
        return window.selectedTarget.name;
    }
    return null;
}

async function startCallAsCaller(targetUsername, callType) {
    if (!targetUsername) {
        alert("Select a user to call");
        return;
    }

    if (isCallActive) {
        alert("You are already in a call");
        return;
    }

    // Check if browser supports getUserMedia
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        alert("Your browser does not support video/audio calls. Please use a modern browser like Chrome, Firefox, or Edge.");
        return;
    }

    currentCallPartner = targetUsername;
    currentCallType = callType;

    try {
        // Request permissions first
        localStream = await navigator.mediaDevices.getUserMedia({
            audio: true,
            video: callType === "video"
        });

        isCallActive = true;

        const localVideoEl = document.getElementById("localVideo");
        if (localVideoEl) localVideoEl.srcObject = localStream;

        await createPeerConnection();

        localStream.getTracks().forEach(track => pc.addTrack(track, localStream));

        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);

        await callHubConnection.invoke("CallUser", targetUsername, callType);
        await callHubConnection.invoke("SendOffer", targetUsername, JSON.stringify(offer));

        showCallUI(targetUsername);
    } catch (err) {
        console.error("startCallAsCaller error:", err);
        if (err.name === 'NotAllowedError' || err.name === 'PermissionDeniedError') {
            alert("Camera/microphone permission denied. Please allow access and try again.");
        } else if (err.name === 'NotFoundError') {
            alert("No camera or microphone found. Please connect a device and try again.");
        } else {
            alert("Failed to start call: " + err.message);
        }
        cleanupCall();
    }
}

async function createPeerConnection() {
    pc = new RTCPeerConnection(rtcConfig);

    remoteStream = new MediaStream();
    const remoteVideoEl = document.getElementById("remoteVideo");
    if (remoteVideoEl) remoteVideoEl.srcObject = remoteStream;

    pc.ontrack = (evt) => {
        evt.streams.forEach(s => {
            s.getTracks().forEach(t => {
                if (!remoteStream.getTracks().includes(t)) {
                    remoteStream.addTrack(t);
                }
            });
        });
    };

    pc.onicecandidate = (evt) => {
        if (evt.candidate && currentCallPartner) {
            callHubConnection.invoke("SendIceCandidate", currentCallPartner, JSON.stringify(evt.candidate))
                .catch(err => console.error("Failed to send ICE candidate:", err));
        }
    };

    pc.onconnectionstatechange = () => {
        console.log("PC state:", pc.connectionState);
        if (pc && (pc.connectionState === "disconnected" || pc.connectionState === "failed" || pc.connectionState === "closed")) {
            cleanupCall();
        }
    };

    pc.oniceconnectionstatechange = () => {
        console.log("ICE state:", pc.iceConnectionState);
    };
}

async function acceptCall() {
    const incomingModal = document.getElementById("incomingCallModal");
    if (incomingModal) incomingModal.classList.remove('show');

    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        alert("Your browser does not support video/audio calls.");
        rejectCall();
        return;
    }

    try {
        localStream = await navigator.mediaDevices.getUserMedia({
            audio: true,
            video: currentCallType === "video"
        });

        isCallActive = true;

        const localVideoEl = document.getElementById("localVideo");
        if (localVideoEl) localVideoEl.srcObject = localStream;

        await createPeerConnection();

        localStream.getTracks().forEach(track => pc.addTrack(track, localStream));

        if (pendingOffer) {
            await pc.setRemoteDescription(new RTCSessionDescription(pendingOffer));
            pendingOffer = null;
        }

        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);

        await callHubConnection.invoke("AcceptCall", currentCallPartner);
        await callHubConnection.invoke("SendAnswer", currentCallPartner, JSON.stringify(answer));

        showCallUI(currentCallPartner);
    } catch (err) {
        console.error("acceptCall error:", err);
        if (err.name === 'NotAllowedError' || err.name === 'PermissionDeniedError') {
            alert("Camera/microphone permission denied.");
        } else {
            alert("Failed to accept call: " + err.message);
        }
        cleanupCall();
    }
}

async function rejectCall() {
    const incomingModal = document.getElementById("incomingCallModal");
    if (incomingModal) incomingModal.classList.remove('show');

    if (currentCallPartner) {
        await callHubConnection.invoke("RejectCall", currentCallPartner).catch(console.error);
    }
    currentCallPartner = null;
    currentCallType = null;
    pendingOffer = null;
}

async function hangup() {
    if (currentCallPartner) {
        await callHubConnection.invoke("Hangup", currentCallPartner).catch(console.error);
    }
    cleanupCall();
}

function cleanupCall() {
    try {
        if (pc) {
            pc.ontrack = null;
            pc.onicecandidate = null;
            pc.onconnectionstatechange = null;
            pc.oniceconnectionstatechange = null;
            try { pc.close(); } catch (e) { console.error("Error closing peer connection:", e); }
            pc = null;
        }

        if (localStream) {
            localStream.getTracks().forEach(t => {
                try { t.stop(); } catch (e) { console.error("Error stopping track:", e); }
            });
            localStream = null;
            const localVideoEl = document.getElementById("localVideo");
            if (localVideoEl) localVideoEl.srcObject = null;
        }

        if (remoteStream) {
            remoteStream.getTracks().forEach(t => {
                try { t.stop(); } catch (e) { console.error("Error stopping remote track:", e); }
            });
            remoteStream = null;
            const remoteVideoEl = document.getElementById("remoteVideo");
            if (remoteVideoEl) remoteVideoEl.srcObject = null;
        }

        currentCallPartner = null;
        currentCallType = null;
        pendingOffer = null;
        isCallActive = false;

        const callContainer = document.getElementById("callContainer");
        if (callContainer) callContainer.style.display = "none";
    } catch (e) {
        console.warn("cleanup error", e);
    }
}

function showCallUI(partner) {
    const callWithLabel = document.getElementById("callWithLabel");
    const callContainer = document.getElementById("callContainer");

    if (callWithLabel) callWithLabel.textContent = `Call with ${partner}`;
    if (callContainer) callContainer.style.display = "block";
}

// SignalR handlers
callHubConnection.on("IncomingCall", async (fromUsername, callType) => {
    currentCallPartner = fromUsername;
    currentCallType = callType;

    if (isCallActive || pc) {
        await callHubConnection.invoke("RejectCall", fromUsername).catch(console.error);
        return;
    }

    const incomingText = document.getElementById("incomingCallText");
    const incomingModal = document.getElementById("incomingCallModal");

    if (incomingText) incomingText.textContent = `${fromUsername} is calling (${callType})`;
    if (incomingModal) incomingModal.classList.add('show');
});

callHubConnection.on("CallFailed", (targetUser, reason) => {
    alert("Call failed: " + reason);
    cleanupCall();
});

callHubConnection.on("CallAccepted", async (byUsername) => {
    console.log("Call accepted by", byUsername);
});

callHubConnection.on("CallRejected", (byUsername) => {
    alert(`${byUsername} rejected the call.`);
    cleanupCall();
});

callHubConnection.on("CallEnded", (byUsername) => {
    alert(`${byUsername} ended the call.`);
    cleanupCall();
});

callHubConnection.on("ReceiveOffer", async (fromUsername, offerJson) => {
    try {
        const offerDesc = JSON.parse(offerJson);
        pendingOffer = offerDesc;

        if (!currentCallPartner) {
            currentCallPartner = fromUsername;
        }
    } catch (err) {
        console.error("Error receiving offer:", err);
    }
});

callHubConnection.on("ReceiveAnswer", async (fromUsername, answerJson) => {
    if (!pc) {
        console.warn("Received answer but no peer connection exists");
        return;
    }

    try {
        const ans = JSON.parse(answerJson);
        await pc.setRemoteDescription(new RTCSessionDescription(ans));
        console.log("Remote description set successfully");
    } catch (err) {
        console.error("Error setting remote description:", err);
    }
});

callHubConnection.on("ReceiveIceCandidate", async (fromUsername, candidateJson) => {
    if (!pc) {
        console.warn("Received ICE candidate but no peer connection exists");
        return;
    }

    try {
        const candidate = JSON.parse(candidateJson);
        await pc.addIceCandidate(new RTCIceCandidate(candidate));
    } catch (e) {
        console.error("addIceCandidate error", e);
    }
});

// Initialize when DOM is ready
function initCallButtons() {
    const voiceBtn = document.getElementById("voiceCallBtn");
    const videoBtn = document.getElementById("videoCallBtn");
    const acceptBtn = document.getElementById("acceptCallBtn");
    const rejectBtn = document.getElementById("rejectCallBtn");
    const hangupBtn = document.getElementById("hangupBtn");

    if (voiceBtn) {
        voiceBtn.addEventListener("click", async () => {
            const target = getSelectedChatTargetUsername();
            if (!target) {
                alert("Select a user to call (click a user from the list)");
                return;
            }
            await startCallAsCaller(target, "audio");
        });
    }

    if (videoBtn) {
        videoBtn.addEventListener("click", async () => {
            const target = getSelectedChatTargetUsername();
            if (!target) {
                alert("Select a user to call (click a user from the list)");
                return;
            }
            await startCallAsCaller(target, "video");
        });
    }

    if (acceptBtn) acceptBtn.addEventListener("click", acceptCall);
    if (rejectBtn) rejectBtn.addEventListener("click", rejectCall);
    if (hangupBtn) hangupBtn.addEventListener("click", hangup);
}

// Start hub connection
(async function startHub() {
    try {
        await callHubConnection.start();
        console.log("CallHub connected successfully");

        // Initialize buttons after connection is established
        initCallButtons();
    } catch (err) {
        console.error("CallHub connection error:", err);
        setTimeout(startHub, 5000);
    }
})();

// Handle reconnection
callHubConnection.onreconnecting((error) => {
    console.warn("CallHub reconnecting:", error);
});

callHubConnection.onreconnected((connectionId) => {
    console.log("CallHub reconnected:", connectionId);
});

callHubConnection.onclose((error) => {
    console.error("CallHub connection closed:", error);
    cleanupCall();
    setTimeout(startHub, 5000);
});