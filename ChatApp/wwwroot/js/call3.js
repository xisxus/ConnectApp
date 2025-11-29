// wwwroot/js/call.js
"use strict";

const callHubConnection = new signalR.HubConnectionBuilder()
    .withUrl("/callHub")
    .withAutomaticReconnect()
    .build();

let pc = null;                 // RTCPeerConnection
let localStream = null;
let remoteStream = null;
let currentCallPartner = null; // username
let currentCallType = null;    // "audio" or "video"
let pendingOffer = null;       // *** ADDED: Store offer until user accepts ***

// STUN servers - add TURN if needed for NAT traversal in production
const rtcConfig = {
    iceServers: [
        { urls: "stun:stun.l.google.com:19302" }
    ]
};

// UI elements (assumes IDs from Chat.cshtml)
const voiceBtn = document.getElementById("voiceCallBtn");
const videoBtn = document.getElementById("videoCallBtn");
const incomingModal = document.getElementById("incomingCallModal");
const incomingText = document.getElementById("incomingCallText");
const acceptBtn = document.getElementById("acceptCallBtn");
const rejectBtn = document.getElementById("rejectCallBtn");
const callContainer = document.getElementById("callContainer");
const callWithLabel = document.getElementById("callWithLabel");
const hangupBtn = document.getElementById("hangupBtn");
const localVideoEl = document.getElementById("localVideo");
const remoteVideoEl = document.getElementById("remoteVideo");
//const targetLabel = document.getElementById("targetLabel");

// Helper to get the currently selected chat target username from your chat UI.
function getSelectedChatTargetUsername() {
    if (window.selectedTarget && window.selectedTarget.type === 'user') {
        return window.selectedTarget.name;
    }
    return null;
}

async function startCallAsCaller(targetUsername, callType) {
    if (!targetUsername) { alert("Select a user to call"); return; }
    currentCallPartner = targetUsername;
    currentCallType = callType;

    try {
        // Acquire local media (audio or audio+video)
        localStream = await navigator.mediaDevices.getUserMedia({
            audio: true,
            video: callType === "video"
        });

        // show local preview
        if (localVideoEl) localVideoEl.srcObject = localStream;

        // create RTCPeerConnection
        await createPeerConnection();

        // add tracks
        localStream.getTracks().forEach(track => pc.addTrack(track, localStream));

        // create offer
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);

        // notify target via SignalR
        await callHubConnection.invoke("CallUser", targetUsername, callType);
        // send offer
        await callHubConnection.invoke("SendOffer", targetUsername, JSON.stringify(offer));

        // show local call window
        showCallUI(targetUsername);
    } catch (err) {
        console.error("startCallAsCaller error:", err);
        cleanupCall();
    }
}

async function createPeerConnection() {
    pc = new RTCPeerConnection(rtcConfig);

    // ensure remote stream container
    remoteStream = new MediaStream();
    if (remoteVideoEl) remoteVideoEl.srcObject = remoteStream;

    // ontrack collects remote media
    pc.ontrack = (evt) => {
        evt.streams.forEach(s => {
            s.getTracks().forEach(t => remoteStream.addTrack(t));
        });
    };

    pc.onicecandidate = (evt) => {
        if (evt.candidate && currentCallPartner) {
            callHubConnection.invoke("SendIceCandidate", currentCallPartner, JSON.stringify(evt.candidate)).catch(console.error);
        }
    };

    pc.onconnectionstatechange = () => {
        console.log("PC state:", pc.connectionState);
        if (pc && (pc.connectionState === "disconnected" || pc.connectionState === "failed" || pc.connectionState === "closed")) {
            cleanupCall();
        }
    };
}

// Accept call (callee)
// *** FIXED: Now properly sets remote description before creating answer ***
async function acceptCall() {
    incomingModal.style.display = "none";

    try {
        // get local media - use video for both audio and video calls
        localStream = await navigator.mediaDevices.getUserMedia({
            audio: true,
            video: currentCallType === "video"
        });

        if (localVideoEl) localVideoEl.srcObject = localStream;

        await createPeerConnection();

        // add tracks
        localStream.getTracks().forEach(track => pc.addTrack(track, localStream));

        // *** FIXED: Set remote description from pending offer ***
        if (pendingOffer) {
            await pc.setRemoteDescription(pendingOffer);
            pendingOffer = null; // clear it
        }

        // create answer
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);

        // *** ADDED: Notify caller that call was accepted ***
        await callHubConnection.invoke("AcceptCall", currentCallPartner);

        // send answer to caller
        await callHubConnection.invoke("SendAnswer", currentCallPartner, JSON.stringify(answer));
        showCallUI(currentCallPartner);
    } catch (err) {
        console.error("acceptCall error:", err);
        cleanupCall();
    }
}

// Reject call (callee)
async function rejectCall() {
    incomingModal.style.display = "none";
    if (currentCallPartner) {
        await callHubConnection.invoke("RejectCall", currentCallPartner).catch(console.error);
        currentCallPartner = null;
        pendingOffer = null; // *** ADDED: Clear pending offer ***
    }
}

// Hang up (either side)
async function hangup() {
    if (currentCallPartner) await callHubConnection.invoke("Hangup", currentCallPartner).catch(console.error);
    cleanupCall();
}

// cleanup local resources and UI
function cleanupCall() {
    try {
        if (pc) {
            pc.ontrack = null;
            pc.onicecandidate = null;
            try { pc.close(); } catch { }
            pc = null;
        }

        if (localStream) {
            localStream.getTracks().forEach(t => t.stop());
            localStream = null;
            if (localVideoEl) localVideoEl.srcObject = null;
        }

        if (remoteStream) {
            remoteStream.getTracks().forEach(t => t.stop());
            remoteStream = null;
            if (remoteVideoEl) remoteVideoEl.srcObject = null;
        }

        currentCallPartner = null;
        currentCallType = null;
        pendingOffer = null; // *** ADDED: Clear pending offer ***
        callContainer.style.display = "none";
    } catch (e) {
        console.warn("cleanup error", e);
    }
}

function showCallUI(partner) {
    callWithLabel.textContent = `Call with ${partner}`;
    callContainer.style.display = "block";
}

// ---------- SignalR client handlers ----------

callHubConnection.on("IncomingCall", async (fromUsername, callType) => {
    // Save callType and current partner
    currentCallPartner = fromUsername;
    currentCallType = callType;
    // If you're busy in another call, optionally auto-reject
    if (pc) {
        // busy
        await callHubConnection.invoke("RejectCall", fromUsername).catch(console.error);
        return;
    }
    // Show incoming UI
    incomingText.textContent = `${fromUsername} is calling (${callType})`;
    incomingModal.style.display = "block";
});

callHubConnection.on("CallFailed", (targetUser, reason) => {
    alert("Call failed: " + reason);
});

callHubConnection.on("CallAccepted", async (byUsername) => {
    // caller: callee accepted
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

// *** FIXED: Store offer instead of immediately setting it ***
callHubConnection.on("ReceiveOffer", async (fromUsername, offerJson) => {
    currentCallPartner = fromUsername;

    // Parse and store the offer
    const offerDesc = JSON.parse(offerJson);
    pendingOffer = offerDesc;

    // Show incoming modal - user will accept/reject
    // (Modal already shown by IncomingCall event, but just in case)
    if (incomingModal.style.display !== "block") {
        incomingText.textContent = `${fromUsername} is calling`;
        incomingModal.style.display = "block";
    }
});

callHubConnection.on("ReceiveAnswer", async (fromUsername, answerJson) => {
    if (!pc) return;
    const ans = JSON.parse(answerJson);
    await pc.setRemoteDescription(ans);
});

callHubConnection.on("ReceiveIceCandidate", async (fromUsername, candidateJson) => {
    if (!pc) return;
    try {
        const candidate = JSON.parse(candidateJson);
        await pc.addIceCandidate(candidate);
    } catch (e) {
        console.error("addIceCandidate error", e);
    }
});

// ---------- wire UI buttons ----------

if (voiceBtn) {
    voiceBtn.addEventListener("click", async () => {
        const target = getSelectedChatTargetUsername();
        if (!target) { alert("Select a user to call (click a user from the list)"); return; }
        await startCallAsCaller(target, "audio");
    });
}
if (videoBtn) {
    videoBtn.addEventListener("click", async () => {
        const target = getSelectedChatTargetUsername();
        if (!target) { alert("Select a user to call (click a user from the list)"); return; }
        await startCallAsCaller(target, "video");
    });
}

if (acceptBtn) acceptBtn.addEventListener("click", acceptCall);
if (rejectBtn) rejectBtn.addEventListener("click", rejectCall);
if (hangupBtn) hangupBtn.addEventListener("click", hangup);

// Start the hub connection
(async function startHub() {
    try {
        await callHubConnection.start();
        console.log("CallHub connected");
    } catch (err) {
        console.error("CallHub start error:", err);
        setTimeout(startHub, 2000);
    }
})();